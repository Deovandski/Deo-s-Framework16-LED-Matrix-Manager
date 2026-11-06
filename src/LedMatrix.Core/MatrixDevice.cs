namespace LedMatrix.Core;

public enum DeviceStatus
{
    Connecting,
    Connected,
    Asleep,
    PortBusy,
    Disconnected,
}

/// <summary>
/// One physical LED Matrix module. All serial I/O happens on a dedicated background thread;
/// UI code only sets the desired state (frame, brightness, awake) and the worker syncs it to
/// the hardware. While the LEDs should be off nothing but Sleep(true) is sent, because any
/// command wakes the firmware. While awake, a keep-alive beats the firmware's 60 s idle timer.
/// </summary>
public sealed class MatrixDevice : IDisposable
{
    public static readonly TimeSpan KeepAliveInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ResponseTimeout = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan MaxReconnectDelay = TimeSpan.FromSeconds(10);

    private readonly ISerialTransport _transport;
    private readonly TimeProvider _time;
    private readonly object _gate = new();
    private readonly AutoResetEvent _signal = new(false);
    private Thread? _thread;
    private volatile bool _stopping;

    // Desired state (guarded by _gate)
    private Frame _frame = new();
    private byte _brightness = 100;
    private bool _awake = true;
    private bool _frameDirty = true;
    private bool _brightnessDirty = true;
    private bool _identifyRequested;
    private ushort? _scrollPeriod;
    private bool _scrollDirty;

    // Worker-only state
    private bool _sleepSent;
    private bool _needsWake = true;
    private bool _versionQueried;
    private DateTimeOffset _lastWrite = DateTimeOffset.MinValue;
    private DateTimeOffset _nextOpenAttempt = DateTimeOffset.MinValue;
    private TimeSpan _reconnectDelay = TimeSpan.FromMilliseconds(500);
    private DeviceStatus _status = DeviceStatus.Connecting;

    public MatrixDevice(string serialNumber, ISerialTransport transport, TimeProvider? time = null)
    {
        SerialNumber = serialNumber;
        _transport = transport;
        _time = time ?? TimeProvider.System;
    }

    public string SerialNumber { get; }

    public string PortName => _transport.PortName;

    public FirmwareVersion? Version { get; private set; }

    public DeviceStatus Status => _status;

    /// <summary>Raised on the worker thread whenever <see cref="Status"/> or <see cref="Version"/> changes.</summary>
    public event EventHandler? StatusChanged;

    public void Start()
    {
        if (_thread is not null)
        {
            return;
        }

        _thread = new Thread(WorkerLoop)
        {
            IsBackground = true,
            Name = $"LedMatrix {PortName}",
            Priority = ThreadPriority.BelowNormal,
        };
        _thread.Start();
    }

    public void SetFrame(Frame frame)
    {
        lock (_gate)
        {
            _frame = frame.Clone();
            _frameDirty = true;
        }

        _signal.Set();
    }

    public void SetBrightness(byte brightness)
    {
        lock (_gate)
        {
            if (_brightness == brightness)
            {
                return;
            }

            _brightness = brightness;
            _brightnessDirty = true;
        }

        _signal.Set();
    }

    /// <summary>
    /// Firmware scroll: non-null makes the module scroll the current frame down by itself, one row every
    /// N ms, with no further traffic. Null stops it (needed before streaming app-driven frames).
    /// </summary>
    public void SetScroll(ushort? periodMs)
    {
        lock (_gate)
        {
            if (_scrollPeriod == periodMs)
            {
                return;
            }

            _scrollPeriod = periodMs;
            _scrollDirty = true;
        }

        _signal.Set();
    }

    /// <summary>Gate for all traffic. False = fade out and stay silent; true = wake and restore content.</summary>
    public void SetAwake(bool awake)
    {
        lock (_gate)
        {
            _awake = awake;
        }

        _signal.Set();
    }

    /// <summary>Blinks the module a few times so the user can see which one it is.</summary>
    public void Identify()
    {
        lock (_gate)
        {
            _identifyRequested = true;
        }

        _signal.Set();
    }

    /// <summary>Forces brightness and content to be re-sent (e.g. after resume from sleep).</summary>
    public void Resync()
    {
        lock (_gate)
        {
            _frameDirty = true;
            _brightnessDirty = true;
            _scrollDirty = true;
        }

        _needsWake = true;
        _signal.Set();
    }

    public void Dispose()
    {
        _stopping = true;
        _signal.Set();
        _thread?.Join(TimeSpan.FromSeconds(2));
        try
        {
            _transport.Dispose();
        }
        catch (Exception ex)
        {
            Log.Warn($"Closing {PortName} failed", ex);
        }

        _signal.Dispose();
    }

    private void WorkerLoop()
    {
        while (!_stopping)
        {
            _signal.WaitOne(TimeSpan.FromSeconds(1));
            if (_stopping)
            {
                break;
            }

            RunOnce();
        }
    }

    /// <summary>One synchronisation pass. Public to tests via InternalsVisibleTo.</summary>
    internal void RunOnce()
    {
        try
        {
            if (!_transport.IsOpen && !TryOpen())
            {
                return;
            }

            Sync();
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or UnauthorizedAccessException or InvalidOperationException)
        {
            Log.Warn($"{PortName}: I/O failed, reconnecting", ex);
            SafeClose();
            SetStatus(DeviceStatus.Disconnected);
            ScheduleReconnect();
        }
    }

    private bool TryOpen()
    {
        var now = _time.GetUtcNow();
        if (now < _nextOpenAttempt)
        {
            return false;
        }

        try
        {
            _transport.Open();
        }
        catch (UnauthorizedAccessException)
        {
            Log.Info($"{PortName}: port is in use by another application");
            SetStatus(DeviceStatus.PortBusy);
            ScheduleReconnect();
            return false;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or InvalidOperationException)
        {
            Log.Warn($"{PortName}: open failed", ex);
            SetStatus(DeviceStatus.Disconnected);
            ScheduleReconnect();
            return false;
        }

        _reconnectDelay = TimeSpan.FromMilliseconds(500);
        Log.Info($"{PortName}: opened (serial {SerialNumber})");

        // The module may have been asleep (idle timeout) or showing old content: resend everything.
        // The version is queried on the first awake pass, because any command (including the
        // version query) wakes the LEDs, which we must not do while they are supposed to be off.
        _versionQueried = false;
        _needsWake = true;
        _sleepSent = false;
        lock (_gate)
        {
            _frameDirty = true;
            _brightnessDirty = true;
            _scrollDirty = true;
        }

        SetStatus(DeviceStatus.Connected);
        return true;
    }

    private FirmwareVersion? QueryVersion()
    {
        try
        {
            _transport.DiscardInput();
            Write(Protocol.GetVersion());
            var response = new byte[Protocol.ResponseLength];
            var chunk = new byte[Protocol.ResponseLength];
            var total = 0;
            var deadline = _time.GetUtcNow() + ResponseTimeout;
            while (total < response.Length && _time.GetUtcNow() < deadline)
            {
                var read = _transport.Read(chunk, ResponseTimeout);
                if (read <= 0)
                {
                    break;
                }

                var n = Math.Min(read, response.Length - total);
                Array.Copy(chunk, 0, response, total, n);
                total += n;
            }

            return total >= 3 ? Protocol.ParseVersion(response) : null;
        }
        catch (TimeoutException)
        {
            return null;
        }
    }

    private void Sync()
    {
        bool awake, identify, frameDirty, brightnessDirty, scrollDirty;
        Frame frame;
        byte brightness;
        ushort? scroll;
        lock (_gate)
        {
            awake = _awake;
            identify = _identifyRequested;
            frameDirty = _frameDirty;
            brightnessDirty = _brightnessDirty;
            scrollDirty = _scrollDirty;
            frame = _frame;
            brightness = _brightness;
            scroll = _scrollPeriod;
            _identifyRequested = false;
            _frameDirty = false;
            _brightnessDirty = false;
            _scrollDirty = false;
        }

        if (!awake)
        {
            if (!_sleepSent)
            {
                Write(Protocol.SetSleep(true));
                _sleepSent = true;
                _needsWake = true;
            }

            // Remember that content changed while asleep, so it is sent on wake.
            if (frameDirty || brightnessDirty || scrollDirty)
            {
                lock (_gate)
                {
                    _frameDirty |= frameDirty;
                    _brightnessDirty |= brightnessDirty;
                    _scrollDirty |= scrollDirty;
                }
            }

            SetStatus(DeviceStatus.Asleep);
            return;
        }

        if (!_versionQueried)
        {
            _versionQueried = true;
            Version = QueryVersion();
            Log.Info($"{PortName}: firmware {Version?.ToString() ?? "unknown"}");
            StatusChanged?.Invoke(this, EventArgs.Empty);
        }

        if (_needsWake)
        {
            Write(Protocol.SetSleep(false));
            _needsWake = false;
            _sleepSent = false;
            frameDirty = true;
            brightnessDirty = true;
            scrollDirty = true;
        }

        SetStatus(DeviceStatus.Connected);

        if (identify)
        {
            if (scroll is not null)
            {
                Write(Protocol.SetAnimate(false));
            }

            RunIdentify(brightness);
            frameDirty = true;
            brightnessDirty = true;
            scrollDirty = true;
        }

        // Stop the firmware scroll before sending a new picture, so it isn't rotated mid-update.
        if (scrollDirty && scroll is null)
        {
            Write(Protocol.SetAnimate(false));
        }

        if (brightnessDirty)
        {
            Write(Protocol.SetBrightness(brightness));
        }

        if (frameDirty)
        {
            foreach (var cmd in Protocol.FrameCommands(frame))
            {
                Write(cmd);
            }
        }

        // Start the firmware scroll after the picture is in place.
        if (scrollDirty && scroll is { } period)
        {
            Write(Protocol.SetAnimationPeriod(period));
            Write(Protocol.SetAnimate(true));
        }

        if (_time.GetUtcNow() - _lastWrite >= KeepAliveInterval)
        {
            // Harmless command that resets the firmware idle timer without changing the image.
            Write(Protocol.SetBrightness(brightness));
        }
    }

    private void RunIdentify(byte brightness)
    {
        var full = new Frame();
        full.Fill(255);
        var blank = new Frame();
        Write(Protocol.SetBrightness(Math.Max(brightness, (byte)128)));
        for (var i = 0; i < 4 && !_stopping; i++)
        {
            Write(Protocol.DrawBlackAndWhite(full));
            Thread.Sleep(250);
            Write(Protocol.DrawBlackAndWhite(blank));
            Thread.Sleep(250);
        }
    }

    private void Write(byte[] command)
    {
        _transport.Write(command);
        _lastWrite = _time.GetUtcNow();
    }

    private void ScheduleReconnect()
    {
        _nextOpenAttempt = _time.GetUtcNow() + _reconnectDelay;
        _reconnectDelay = TimeSpan.FromTicks(Math.Min(_reconnectDelay.Ticks * 2, MaxReconnectDelay.Ticks));
    }

    private void SafeClose()
    {
        try
        {
            _transport.Close();
        }
        catch (Exception ex)
        {
            Log.Debug($"{PortName}: close after failure threw {ex.GetType().Name}");
        }
    }

    private void SetStatus(DeviceStatus status)
    {
        if (_status == status)
        {
            return;
        }

        _status = status;
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }
}
