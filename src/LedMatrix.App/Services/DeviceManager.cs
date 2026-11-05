using System.Windows.Threading;
using LedMatrix.Core;

namespace LedMatrix.App.Services;

/// <summary>
/// Owns the Left/Right <see cref="MatrixDevice"/> instances, keeps them in sync with the
/// desired state, and re-detects modules on hot-plug or resume. UI-thread only.
/// </summary>
internal sealed class DeviceManager : IDisposable
{
    private readonly AppSettings _settings;
    private readonly Dispatcher _dispatcher;
    private readonly Dictionary<MatrixSide, MatrixDevice?> _devices = new()
    {
        [MatrixSide.Left] = null,
        [MatrixSide.Right] = null,
    };

    private readonly Dictionary<MatrixSide, LedMatrix.Core.Frame> _frames = new();
    private bool _awake = true;

    public DeviceManager(AppSettings settings, Dispatcher dispatcher)
    {
        _settings = settings;
        _dispatcher = dispatcher;
        _frames[MatrixSide.Left] = settings.GetFrame(MatrixSide.Left);
        _frames[MatrixSide.Right] = settings.GetFrame(MatrixSide.Right);
    }

    /// <summary>Raised on the UI thread when a module appears, disappears or changes status.</summary>
    public event EventHandler? DevicesChanged;

    public MatrixDevice? Get(MatrixSide side) => _devices[side];

    public LedMatrix.Core.Frame GetFrame(MatrixSide side) => _frames[side];

    public void Rescan()
    {
        var modules = DeviceDiscovery.Find();
        var (left, right) = RoleAssigner.Assign(modules.Select(m => m.SerialNumber).ToList(), _settings);
        var changed = Update(MatrixSide.Left, left, modules) | Update(MatrixSide.Right, right, modules);
        Log.Info($"Rescan: {modules.Count} module(s) found. Left={left ?? "-"} Right={right ?? "-"}");
        if (changed)
        {
            DevicesChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Swap()
    {
        RoleAssigner.Swap(_settings);
        // Force both sides to be rebuilt with the swapped serials; drawings stay on their side.
        DisposeDevice(MatrixSide.Left);
        DisposeDevice(MatrixSide.Right);
        Rescan();
        DevicesChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetFrame(MatrixSide side, LedMatrix.Core.Frame frame)
    {
        _frames[side] = frame.Clone();
        _devices[side]?.SetFrame(frame);
    }

    public void SetBrightness(byte brightness)
    {
        foreach (var d in _devices.Values)
        {
            d?.SetBrightness(brightness);
        }
    }

    public void SetAwake(bool awake)
    {
        _awake = awake;
        foreach (var d in _devices.Values)
        {
            d?.SetAwake(awake);
        }
    }

    public void ResyncAll()
    {
        foreach (var d in _devices.Values)
        {
            d?.Resync();
        }
    }

    public void Identify(MatrixSide side) => _devices[side]?.Identify();

    public void Dispose()
    {
        DisposeDevice(MatrixSide.Left);
        DisposeDevice(MatrixSide.Right);
    }

    private bool Update(MatrixSide side, string? serial, IReadOnlyList<DiscoveredModule> modules)
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

        DisposeDevice(side);
        if (module is null)
        {
            return true;
        }

        var device = new MatrixDevice(module.SerialNumber, new SerialPortTransport(module.PortName));
        device.SetBrightness(_settings.Brightness);
        device.SetFrame(_frames[side]);
        device.SetAwake(_awake);
        device.StatusChanged += OnDeviceStatusChanged;
        _devices[side] = device;
        device.Start();
        return true;
    }

    private void DisposeDevice(MatrixSide side)
    {
        var d = _devices[side];
        if (d is null)
        {
            return;
        }

        _devices[side] = null;
        d.StatusChanged -= OnDeviceStatusChanged;
        d.Dispose();
    }

    private void OnDeviceStatusChanged(object? sender, EventArgs e) =>
        _dispatcher.InvokeAsync(() => DevicesChanged?.Invoke(this, EventArgs.Empty));
}
