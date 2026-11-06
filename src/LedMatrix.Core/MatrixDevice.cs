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
    private static readonly TimeSpan MinWait = TimeSpan.FromMilliseconds(50);

    private readonly object _gate = new();
    private readonly AutoResetEvent _signal = new(false);
    private readonly byte[] _buffer = new byte[Protocol.MaxCommandLength];
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
    private bool _forceFrame;

    // Worker-only state
    private bool _sleepSent;
    private bool _needsWake = true;
    private bool _versionQueried;
    private Frame? _lastSentFrame; // what the module shows, if no firmware scroll moved it since
    private bool _scrollRunning = true; // unknown counts as running (another tool may have left one on)
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

    /// <summary>
    /// Sets the picture and the firmware scroll together, so the worker never acts on half an update
    /// (one lock, one wake-up). See <see cref="SetScroll"/> for <paramref name="scrollPeriodMs"/>.
    /// </summary>
    public void SetContent(Frame frame, ushort? scrollPeriodMs)
    {
        lock (_gate)
        {
            _frame = frame.Clone();
            _frameDirty = true;
            if (_scrollPeriod != scrollPeriodMs)
            {
                _scrollPeriod = scrollPeriodMs;
                _scrollDirty = true;
            }
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
            _forceFrame = true;
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
            RunOnce();
            if (_stopping)
            {
                break;
            }

            // Sleep until there's something to do: new state (signalled), the next reconnect attempt
            // or the next keep-alive. While the LEDs are off it sleeps until signalled.
            var wait = NextWait();
            if (wait != TimeSpan.Zero)
            {
                _signal.WaitOne(wait);
            }
        }
    }

    /// <summary>How long the worker may sleep before it has work of its own (Timeout.InfiniteTimeSpan = until signalled).</summary>
    internal TimeSpan NextWait()
    {
        var now = _time.GetUtcNow();
        if (!_transport.IsOpen)
        {
            var untilOpen = _nextOpenAttempt - now;
            return untilOpen < MinWait ? MinWait : untilOpen;
        }

        if (_sleepSent)
        {
            return Timeout.InfiniteTimeSpan;
        }

        var untilKeepAlive = KeepAliveInterval - (now - _lastWrite);
        return untilKeepAlive < MinWait ? MinWait : untilKeepAlive;
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
            if (_status != DeviceStatus.PortBusy)
            {
                Log.Info($"{PortName}: port is in use by another application; retrying quietly");
            }

            SetStatus(DeviceStatus.PortBusy);
            ScheduleReconnect();
            return false;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or InvalidOperationException)
        {
            if (_status != DeviceStatus.Disconnected)
            {
                Log.Warn($"{PortName}: open failed; retrying quietly", ex);
            }

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
        _lastSentFrame = null;
        _scrollRunning = true;
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
        bool awake, identify, frameDirty, brightnessDirty, scrollDirty, forceFrame;
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
            forceFrame = _forceFrame;
            frame = _frame;
            brightness = _brightness;
            scroll = _scrollPeriod;
            _identifyRequested = false;
            _frameDirty = false;
            _brightnessDirty = false;
            _scrollDirty = false;
            _forceFrame = false;
        }

        if (forceFrame)
        {
            _lastSentFrame = null;
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
                    _forceFrame |= forceFrame;
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
            _lastSentFrame = null;
            frameDirty = true;
            brightnessDirty = true;
            scrollDirty = true;
        }

        SetStatus(DeviceStatus.Connected);

        if (identify)
        {
            if (_scrollRunning)
            {
                Write(Protocol.SetAnimate(false));
                _scrollRunning = false;
            }

            RunIdentify(brightness);
            _lastSentFrame = null;
            frameDirty = true;
            brightnessDirty = true;
            scrollDirty = true;
        }

        // A scrolling module gets a new picture by stopping the scroll, drawing, and restarting it,
        // so the picture isn't rotated mid-update and the scroll starts from the new picture's top.
        var restartScroll = scroll is not null && (scrollDirty || frameDirty);
        if (_scrollRunning && ((scrollDirty && scroll is null) || restartScroll))
        {
            Write(Protocol.SetAnimate(false));
            _scrollRunning = false;
        }

        if (brightnessDirty)
        {
            Write(Protocol.SetBrightness(brightness));
        }

        // Skip a picture the module already shows (no firmware scroll involved).
        if (frameDirty && !scrollDirty && scroll is null && frame.Equals(_lastSentFrame))
        {
            frameDirty = false;
        }

        if (frameDirty)
        {
            SendFrame(frame);
            _lastSentFrame = frame;
        }

        // Start the firmware scroll after the picture is in place.
        if (restartScroll && scroll is { } period)
        {
            Write(Protocol.SetAnimationPeriod(period));
            Write(Protocol.SetAnimate(true));
            _scrollRunning = true;
            _lastSentFrame = null; // the module's picture moves from here on
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

    /// <summary>One DrawBW command for black/white pictures, else 9 StageColumn + FlushColumns; no allocations.</summary>
    private void SendFrame(Frame frame)
    {
        if (frame.IsBlackAndWhite())
        {
            Write(_buffer.AsSpan(0, Protocol.EncodeDrawBlackAndWhite(frame, _buffer)));
            return;
        }

        for (var x = 0; x < Frame.Width; x++)
        {
            Write(_buffer.AsSpan(0, Protocol.EncodeStageColumn(frame, x, _buffer)));
        }

        Write(_buffer.AsSpan(0, Protocol.EncodeFlushColumns(_buffer)));
    }

    private void Write(ReadOnlySpan<byte> command)
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
