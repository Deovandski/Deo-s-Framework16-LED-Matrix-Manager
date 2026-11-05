using System.Runtime.InteropServices;
using System.Windows.Threading;
using LedMatrix.App.Interop;
using LedMatrix.Core;
using static LedMatrix.App.Interop.NativeMethods;

namespace LedMatrix.App.Services;

/// <summary>
/// Watches everything that tells us whether the laptop's own screen is in use:
/// display topology (WM_DISPLAYCHANGE + QueryDisplayConfig), lid switch, session display
/// power (screen timeout), session lock, and suspend/resume. Fully event-driven; changes are
/// debounced so docking/undocking doesn't make the LEDs flicker.
/// </summary>
internal sealed class ScreenStateMonitor : IDisposable
{
    private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(500);

    private readonly MessageWindow _window;
    private readonly DispatcherTimer _debounce;
    private readonly List<IntPtr> _powerRegistrations = [];
    private readonly bool _wtsRegistered;

    private bool? _lidOpen;
    private DisplayPower _displayPower = DisplayPower.Unknown;
    private bool _locked;
    private bool _suspending;

    public ScreenStateMonitor(MessageWindow window)
    {
        _window = window;
        _debounce = new DispatcherTimer { Interval = Debounce };
        _debounce.Tick += (_, _) =>
        {
            _debounce.Stop();
            Publish();
        };

        Current = new ScreenState(QueryInternalPanelActive(), null, DisplayPower.Unknown, false, false);
        _window.Message += OnMessage;

        Register(GUID_LIDSWITCH_STATE_CHANGE);
        Register(GUID_SESSION_DISPLAY_STATUS);
        _wtsRegistered = WTSRegisterSessionNotification(_window.Handle, NOTIFY_FOR_THIS_SESSION);
    }

    public ScreenState Current { get; private set; }

    /// <summary>Raised (debounced, UI thread) when the screen state changes.</summary>
    public event EventHandler? Changed;

    /// <summary>Raised after the PC resumes from sleep; USB devices need re-detection.</summary>
    public event EventHandler? Resumed;

    /// <summary>Raised when Windows reports a device change (hot-plug).</summary>
    public event EventHandler? DevicesChanged;

    public void Dispose()
    {
        _debounce.Stop();
        _window.Message -= OnMessage;
        foreach (var h in _powerRegistrations)
        {
            UnregisterPowerSettingNotification(h);
        }

        if (_wtsRegistered)
        {
            WTSUnRegisterSessionNotification(_window.Handle);
        }
    }

    /// <summary>
    /// True if a built-in panel (eDP / embedded UDI / "internal") is part of the active display
    /// topology. Null if the query failed.
    /// </summary>
    public static bool? QueryInternalPanelActive()
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            if (GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS, out var numPaths, out var numModes) != 0)
            {
                return null;
            }

            var paths = new DISPLAYCONFIG_PATH_INFO[numPaths];
            var modes = new DISPLAYCONFIG_MODE_INFO[numModes];
            var rc = QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS, ref numPaths, paths, ref numModes, modes, IntPtr.Zero);
            if (rc == ERROR_INSUFFICIENT_BUFFER)
            {
                continue; // topology changed between the two calls
            }

            if (rc != 0)
            {
                Log.Warn($"QueryDisplayConfig failed with {rc}");
                return null;
            }

            for (var i = 0; i < numPaths; i++)
            {
                if ((paths[i].flags & DISPLAYCONFIG_PATH_ACTIVE) == 0)
                {
                    continue;
                }

                var tech = paths[i].targetInfo.outputTechnology;
                if (tech is DISPLAYCONFIG_OUTPUT_TECHNOLOGY_DISPLAYPORT_EMBEDDED
                    or DISPLAYCONFIG_OUTPUT_TECHNOLOGY_UDI_EMBEDDED
                    or DISPLAYCONFIG_OUTPUT_TECHNOLOGY_INTERNAL)
                {
                    return true;
                }
            }

            return false;
        }

        return null;
    }

    private void Register(Guid guid)
    {
        var g = guid;
        var h = RegisterPowerSettingNotification(_window.Handle, ref g, DEVICE_NOTIFY_WINDOW_HANDLE);
        if (h != IntPtr.Zero)
        {
            _powerRegistrations.Add(h);
        }
        else
        {
            Log.Warn($"RegisterPowerSettingNotification({guid}) failed: {Marshal.GetLastWin32Error()}");
        }
    }

    private bool OnMessage(int msg, IntPtr wParam, IntPtr lParam)
    {
        switch (msg)
        {
            case WM_DISPLAYCHANGE:
                Log.Debug("WM_DISPLAYCHANGE");
                Schedule();
                break;

            case WM_DEVICECHANGE:
                DevicesChanged?.Invoke(this, EventArgs.Empty);
                break;

            case WM_WTSSESSION_CHANGE:
                var code = (int)wParam.ToInt64();
                if (code is WTS_SESSION_LOCK or WTS_SESSION_UNLOCK)
                {
                    _locked = code == WTS_SESSION_LOCK;
                    Log.Debug(_locked ? "Session locked" : "Session unlocked");
                    Schedule();
                }

                break;

            case WM_POWERBROADCAST:
                HandlePower((int)wParam.ToInt64(), lParam);
                break;
        }

        return false;
    }

    private void HandlePower(int evt, IntPtr lParam)
    {
        switch (evt)
        {
            case PBT_APMSUSPEND:
                Log.Info("System suspending");
                _suspending = true;
                Publish(); // immediately, no debounce: we are about to sleep
                break;

            case PBT_APMRESUMEAUTOMATIC:
            case PBT_APMRESUMESUSPEND:
                // Both events can arrive for one resume; re-detecting twice is harmless.
                Log.Info("System resumed");
                _suspending = false;
                Resumed?.Invoke(this, EventArgs.Empty);
                Schedule();
                break;

            case PBT_POWERSETTINGCHANGE when lParam != IntPtr.Zero:
                var setting = Marshal.PtrToStructure<POWERBROADCAST_SETTING>(lParam);
                if (setting.PowerSetting == GUID_LIDSWITCH_STATE_CHANGE)
                {
                    _lidOpen = setting.Data != 0;
                    Log.Info(_lidOpen == true ? "Lid opened" : "Lid closed");
                    Schedule();
                }
                else if (setting.PowerSetting == GUID_SESSION_DISPLAY_STATUS)
                {
                    _displayPower = setting.Data switch
                    {
                        0 => DisplayPower.Off,
                        1 => DisplayPower.On,
                        2 => DisplayPower.Dimmed,
                        _ => DisplayPower.Unknown,
                    };
                    Log.Info($"Display power: {_displayPower}");
                    Schedule();
                }

                break;
        }
    }

    private void Schedule()
    {
        _debounce.Stop();
        _debounce.Start();
    }

    private void Publish()
    {
        var next = new ScreenState(QueryInternalPanelActive(), _lidOpen, _displayPower, _locked, _suspending);
        if (next == Current)
        {
            return;
        }

        Log.Info($"Screen state: panel={Fmt(next.InternalPanelActive)} lid={Fmt(next.LidOpen)} display={next.DisplayPower} locked={next.SessionLocked} suspending={next.SystemSuspending}");
        Current = next;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static string Fmt(bool? v) => v switch
    {
        true => "yes",
        false => "no",
        null => "?",
    };
}
