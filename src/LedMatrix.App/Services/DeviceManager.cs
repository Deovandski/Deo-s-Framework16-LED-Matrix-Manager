using System.Windows.Threading;
using LedMatrix.Core;
using LedMatrix.Core.Animation;

namespace LedMatrix.App.Services;

/// <summary>
/// Owns the Left/Right <see cref="MatrixDevice"/> instances and re-detects modules on hot-plug or
/// resume. It is also the animation engine's output: <see cref="Show"/> is called from the engine's
/// clock thread, so device access is guarded by a lock. Rescans happen on the UI thread.
/// </summary>
internal sealed class DeviceManager : IFrameSink, IDisposable
{
    private readonly object _lock = new();
    private readonly AppSettings _settings;
    private readonly Dispatcher _dispatcher;
    private readonly Dictionary<MatrixSide, MatrixDevice?> _devices = new()
    {
        [MatrixSide.Left] = null,
        [MatrixSide.Right] = null,
    };

    private readonly Dictionary<MatrixSide, LedMatrix.Core.Frame> _frames = new()
    {
        [MatrixSide.Left] = new LedMatrix.Core.Frame(),
        [MatrixSide.Right] = new LedMatrix.Core.Frame(),
    };

    private readonly Dictionary<MatrixSide, ushort?> _scroll = new()
    {
        [MatrixSide.Left] = null,
        [MatrixSide.Right] = null,
    };

    private bool _awake = true;

    public DeviceManager(AppSettings settings, Dispatcher dispatcher)
    {
        _settings = settings;
        _dispatcher = dispatcher;
    }

    /// <summary>Raised on the UI thread when a module appears, disappears or changes status.</summary>
    public event EventHandler? DevicesChanged;

    public MatrixDevice? Get(MatrixSide side)
    {
        lock (_lock)
        {
            return _devices[side];
        }
    }

    /// <summary>Raised when the Left/Right serial assignment changed (worth saving).</summary>
    public event EventHandler? RolesChanged;

    public void Rescan()
    {
        var modules = DeviceDiscovery.Find();
        bool changed, rolesChanged;
        string? left, right;
        var old = new List<MatrixDevice>();
        var added = new List<MatrixDevice>();
        lock (_lock)
        {
            var before = (_settings.LeftSerial, _settings.RightSerial);
            (left, right) = RoleAssigner.Assign(modules.Select(m => m.SerialNumber).ToList(), _settings);
            rolesChanged = before != (_settings.LeftSerial, _settings.RightSerial);
            changed = Update(MatrixSide.Left, left, modules, old, added) | Update(MatrixSide.Right, right, modules, old, added);
        }

        // Stopping a device waits for its worker thread, so do it outside the lock: the engine's
        // clock thread must never block on it. New devices start after the old ones let go of their ports.
        DisposeAll(old);
        foreach (var d in added)
        {
            d.Start();
        }
        if (rolesChanged)
        {
            RolesChanged?.Invoke(this, EventArgs.Empty);
        }

        Log.Info($"Rescan: {modules.Count} module(s) found. Left={left ?? "-"} Right={right ?? "-"}");
        if (changed)
        {
            DevicesChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Swap()
    {
        var old = new List<MatrixDevice>();
        lock (_lock)
        {
            RoleAssigner.Swap(_settings);
            // Rebuild both sides with the swapped serials; the content stays on its side of the screen.
            Detach(MatrixSide.Left, old);
            Detach(MatrixSide.Right, old);
        }

        DisposeAll(old);
        Rescan();
        DevicesChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    public void Show(MatrixSide side, LedMatrix.Core.Frame frame, ushort? moduleScrollPeriodMs)
    {
        lock (_lock)
        {
            _frames[side] = frame;
            _scroll[side] = moduleScrollPeriodMs;
            _devices[side]?.SetContent(frame, moduleScrollPeriodMs);
        }
    }

    public void SetBrightness(byte brightness)
    {
        lock (_lock)
        {
            foreach (var d in _devices.Values)
            {
                d?.SetBrightness(brightness);
            }
        }
    }

    public void SetAwake(bool awake)
    {
        lock (_lock)
        {
            _awake = awake;
            foreach (var d in _devices.Values)
            {
                d?.SetAwake(awake);
            }
        }
    }

    public void ResyncAll()
    {
        lock (_lock)
        {
            foreach (var d in _devices.Values)
            {
                d?.Resync();
            }
        }
    }

    public void Identify(MatrixSide side)
    {
        lock (_lock)
        {
            _devices[side]?.Identify();
        }
    }

    public void Dispose()
    {
        var old = new List<MatrixDevice>();
        lock (_lock)
        {
            Detach(MatrixSide.Left, old);
            Detach(MatrixSide.Right, old);
        }

        DisposeAll(old);
    }

    private static void DisposeAll(List<MatrixDevice> devices)
    {
        foreach (var d in devices)
        {
            d.Dispose();
        }
    }

    private static ISerialTransport CreateTransport(string portName) =>
        string.Equals(Environment.GetEnvironmentVariable("LEDMATRIX_SERIAL"), "dotnet", StringComparison.OrdinalIgnoreCase)
            ? new SerialPortTransport(portName)
            : new Win32SerialTransport(portName);

    private bool Update(
        MatrixSide side, string? serial, IReadOnlyList<DiscoveredModule> modules, List<MatrixDevice> old, List<MatrixDevice> added)
    {
        var current = _devices[side];
        var module = serial is null ? null : modules.FirstOrDefault(m => m.SerialNumber == serial);

        if (current is not null && module is not null &&
            current.SerialNumber == module.SerialNumber && current.PortName == module.PortName)
        {
            return false; // unchanged
        }

        if (current is null && module is null)
        {
            return false;
        }

        Detach(side, old);
        if (module is null)
        {
            return true;
        }

        var device = new MatrixDevice(module.SerialNumber, CreateTransport(module.PortName));
        device.SetBrightness(_settings.Brightness);
        device.SetContent(_frames[side], _scroll[side]);
        device.SetAwake(_awake);
        device.StatusChanged += OnDeviceStatusChanged;
        _devices[side] = device;
        added.Add(device);
        return true;
    }

    /// <summary>Takes the device off its side (under the lock); the caller disposes it after releasing the lock.</summary>
    private void Detach(MatrixSide side, List<MatrixDevice> old)
    {
        var d = _devices[side];
        if (d is null)
        {
            return;
        }

        _devices[side] = null;
        d.StatusChanged -= OnDeviceStatusChanged;
        old.Add(d);
    }

    private void OnDeviceStatusChanged(object? sender, EventArgs e) =>
        _dispatcher.InvokeAsync(() => DevicesChanged?.Invoke(this, EventArgs.Empty));
}
