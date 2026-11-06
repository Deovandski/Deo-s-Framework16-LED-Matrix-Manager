using System.IO;
using System.Windows.Threading;
using LedMatrix.App.Interop;
using LedMatrix.App.Services;
using LedMatrix.Core;
using LedMatrix.Core.Animation;
using Frame = LedMatrix.Core.Frame;

namespace LedMatrix.App;

/// <summary>
/// Glue between settings, the animation engine, the LED modules and the screen monitor. UI-thread only.
/// Owns the single decision "should the LEDs be on right now?".
/// </summary>
internal sealed class AppController : IDisposable
{
    public static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DeoLedMatrixManager", "settings.json");

    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _saveTimer;
    private readonly DispatcherTimer _rescanTimer;

    public AppController(MessageWindow window, Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        Settings = SettingsStore.Load(SettingsPath);
        Devices = new DeviceManager(Settings, dispatcher);
        Engine = new AnimationEngine(Devices, stats: new SystemStats());
        Engine.Preview += (side, frame) => _dispatcher.InvokeAsync(() => PreviewFrame?.Invoke(side, frame));

        _saveTimer = new DispatcherTimer(DispatcherPriority.Background, dispatcher) { Interval = TimeSpan.FromSeconds(1) };
        _saveTimer.Tick += (_, _) => SaveNow();

        // Hot-plug produces bursts of WM_DEVICECHANGE; rescan once things settle.
        _rescanTimer = new DispatcherTimer(DispatcherPriority.Background, dispatcher) { Interval = TimeSpan.FromMilliseconds(750) };
        _rescanTimer.Tick += (_, _) =>
        {
            _rescanTimer.Stop();
            Devices.Rescan();
        };

        Devices.DevicesChanged += (_, _) =>
        {
            SaveSoon(); // role assignment may have changed
            StateChanged?.Invoke(this, EventArgs.Empty);
        };

        Screen = new ScreenStateMonitor(window);
        Screen.Changed += (_, _) => ApplyPower();
        Screen.DevicesChanged += (_, _) => ScheduleRescan(TimeSpan.FromMilliseconds(750));
        Screen.Resumed += (_, _) =>
        {
            // USB re-enumerates after resume; give it a moment, then re-detect and resend content.
            Devices.ResyncAll();
            ScheduleRescan(TimeSpan.FromSeconds(2));
        };

        ApplyPower();
        ConfigureEngine();
        Devices.Rescan();
    }

    public AppSettings Settings { get; }

    public DeviceManager Devices { get; }

    public AnimationEngine Engine { get; }

    public ScreenStateMonitor Screen { get; }

    public LedOffReason OffReason { get; private set; } = LedOffReason.None;

    public bool LedsOn => OffReason == LedOffReason.None;

    /// <summary>Raised on the UI thread when anything shown in the UI or tray changes.</summary>
    public event EventHandler? StateChanged;

    /// <summary>Raised on the UI thread with each frame shown on a module (only while previews are enabled).</summary>
    public event Action<MatrixSide, Frame>? PreviewFrame;

    /// <summary>Set while the main window is open, so its grids animate.</summary>
    public bool PreviewEnabled
    {
        get => Engine.PreviewEnabled;
        set => Engine.PreviewEnabled = value;
    }

    /// <summary>Set when the hardware banner is closed; lasts until the app exits (not saved).</summary>
    public bool HardwareBannerClosed { get; set; }

    public string DeviceName(MatrixSide side)
    {
        var serial = Devices.Get(side)?.SerialNumber ?? Settings.GetSerial(side);
        return serial is not null && Settings.DeviceNames.TryGetValue(serial, out var name) && !string.IsNullOrWhiteSpace(name)
            ? name
            : side.ToString();
    }

    public void RenameDevice(MatrixSide side, string name)
    {
        var serial = Devices.Get(side)?.SerialNumber;
        if (serial is null)
        {
            return;
        }

        name = name.Trim();
        if (name.Length == 0 || name == side.ToString())
        {
            Settings.DeviceNames.Remove(serial);
        }
        else
        {
            Settings.DeviceNames[serial] = name;
        }

        SaveSoon();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Stores a hand-drawn (or pattern) picture for a module.</summary>
    public void SetDrawing(MatrixSide side, Frame frame)
    {
        Settings.SetFrame(side, frame);
        ConfigureEngine();
        SaveSoon();
    }

    /// <summary>Changes what a module shows (drawing, animation, text and their options).</summary>
    public void UpdateContent(MatrixSide side, Action<SideContent> change)
    {
        change(Settings.GetContent(side));
        ConfigureEngine();
        SaveSoon();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetLink(LinkMode link)
    {
        if (Settings.Link == link)
        {
            return;
        }

        Settings.Link = link;
        ConfigureEngine();
        SaveSoon();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetKeyboardGap(bool gap)
    {
        Settings.KeyboardGap = gap;
        ConfigureEngine();
        SaveSoon();
    }

    public void SetCrtTransitions(bool on)
    {
        Settings.CrtTransitions = on;
        ConfigureEngine();
        SaveSoon();
    }

    public void SetBrightness(byte brightness)
    {
        Settings.Brightness = brightness;
        Devices.SetBrightness(brightness);
        SaveSoon();
    }

    public void SetBrushBrightness(byte value)
    {
        Settings.BrushBrightness = value;
        SaveSoon();
    }

    public void SetOverride(LedOverride value)
    {
        if (Settings.Override == value)
        {
            return;
        }

        Settings.Override = value;
        Log.Info($"LED mode set to {value}");
        ApplyPower();
        SaveSoon();
    }

    /// <summary>Call after changing <see cref="AppSettings.AutoOff"/>.</summary>
    public void AutoOffChanged()
    {
        ApplyPower();
        SaveSoon();
    }

    public void Swap()
    {
        Devices.Swap();
        SaveSoon();
    }

    public void SaveSoon()
    {
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    public void SaveNow()
    {
        _saveTimer.Stop();
        try
        {
            SettingsStore.Save(SettingsPath, Settings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error("Saving settings failed", ex);
        }
    }

    public void Dispose()
    {
        _rescanTimer.Stop();
        SaveNow();
        Engine.Dispose();
        Screen.Dispose();
        Devices.Dispose();
    }

    private void ConfigureEngine() => Engine.Configure(new EngineConfig
    {
        Link = Settings.Link,
        KeyboardGap = Settings.KeyboardGap,
        CrtTransitions = Settings.CrtTransitions,
        Left = Settings.LeftContent.Clone(),
        Right = Settings.RightContent.Clone(),
        LeftDrawing = Settings.GetFrame(MatrixSide.Left),
        RightDrawing = Settings.GetFrame(MatrixSide.Right),
    });

    private void ScheduleRescan(TimeSpan delay)
    {
        _rescanTimer.Stop();
        _rescanTimer.Interval = delay;
        _rescanTimer.Start();
    }

    private void ApplyPower()
    {
        var reason = ScreenPolicy.Evaluate(Screen.Current, Settings.AutoOff, Settings.Override);
        if (reason != OffReason)
        {
            Log.Info($"LEDs: {ScreenPolicy.Describe(reason)}");
        }

        OffReason = reason;
        var on = reason == LedOffReason.None;

        // Turning off always wins, whatever is playing: the engine stops sending and the modules fade out.
        Engine.OutputEnabled = on;
        Devices.SetAwake(on);
        StateChanged?.Invoke(this, EventArgs.Empty);
    }
}
