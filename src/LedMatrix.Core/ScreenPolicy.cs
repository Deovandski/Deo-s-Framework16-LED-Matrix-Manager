namespace LedMatrix.Core;

public enum DisplayPower
{
    Unknown,
    On,
    Off,
    Dimmed,
}

/// <summary>Manual override from the tray menu.</summary>
public enum LedOverride
{
    Auto,
    AlwaysOn,
    AlwaysOff,
}

/// <summary>Snapshot of everything Windows tells us about the laptop screen. Null = not known yet.</summary>
public sealed record ScreenState(
    bool? InternalPanelActive,
    bool? LidOpen,
    DisplayPower DisplayPower,
    bool SessionLocked,
    bool SystemSuspending)
{
    public static ScreenState Unknown { get; } = new(null, null, DisplayPower.Unknown, false, false);
}

public sealed class AutoOffOptions
{
    /// <summary>Master switch for automatic on/off.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Off when the internal panel is not part of the active display topology (docked, "Second screen only").</summary>
    public bool WhenInternalPanelInactive { get; set; } = true;

    public bool WhenLidClosed { get; set; } = true;

    /// <summary>Off when Windows turns the display off (screen timeout).</summary>
    public bool WhenDisplayOff { get; set; } = true;

    public bool WhenLocked { get; set; }
}

public enum LedOffReason
{
    None,
    ManualOff,
    InternalPanelInactive,
    LidClosed,
    DisplayOff,
    Locked,
    Suspending,
}

public static class ScreenPolicy
{
    /// <summary>Decides whether the LEDs should be lit, and if not, why.</summary>
    public static LedOffReason Evaluate(ScreenState state, AutoOffOptions options, LedOverride manual)
    {
        if (state.SystemSuspending)
        {
            return LedOffReason.Suspending;
        }

        switch (manual)
        {
            case LedOverride.AlwaysOn:
                return LedOffReason.None;
            case LedOverride.AlwaysOff:
                return LedOffReason.ManualOff;
        }

        if (!options.Enabled)
        {
            return LedOffReason.None;
        }

        // Unknown values (null / Unknown) are treated as "on" so we never black out the LEDs by mistake.
        if (options.WhenLidClosed && state.LidOpen == false)
        {
            return LedOffReason.LidClosed;
        }

        if (options.WhenInternalPanelInactive && state.InternalPanelActive == false)
        {
            return LedOffReason.InternalPanelInactive;
        }

        if (options.WhenDisplayOff && state.DisplayPower == DisplayPower.Off)
        {
            return LedOffReason.DisplayOff;
        }

        if (options.WhenLocked && state.SessionLocked)
        {
            return LedOffReason.Locked;
        }

        return LedOffReason.None;
    }

    public static string Describe(LedOffReason reason) => reason switch
    {
        LedOffReason.None => "On",
        LedOffReason.ManualOff => "Off (turned off manually)",
        LedOffReason.InternalPanelInactive => "Off (laptop screen not in use)",
        LedOffReason.LidClosed => "Off (lid closed)",
        LedOffReason.DisplayOff => "Off (screen turned off)",
        LedOffReason.Locked => "Off (PC locked)",
        LedOffReason.Suspending => "Off (going to sleep)",
        _ => reason.ToString(),
    };
}
