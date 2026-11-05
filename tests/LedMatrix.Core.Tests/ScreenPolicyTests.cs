using LedMatrix.Core;

namespace LedMatrix.Core.Tests;

/// <summary>The must-have scenarios from REQUIREMENTS.md FR-23.</summary>
public class ScreenPolicyTests
{
    private static readonly AutoOffOptions Defaults = new();

    private static LedOffReason Eval(ScreenState s, LedOverride o = LedOverride.Auto) =>
        ScreenPolicy.Evaluate(s, Defaults, o);

    private static ScreenState Laptop(bool panel = true, bool lid = true, DisplayPower power = DisplayPower.On, bool locked = false) =>
        new(panel, lid, power, locked, false);

    [Fact]
    public void Normal_laptop_use_is_on() => Assert.Equal(LedOffReason.None, Eval(Laptop()));

    [Fact]
    public void Docked_with_lid_closed_is_off() =>
        Assert.Equal(LedOffReason.LidClosed, Eval(Laptop(panel: false, lid: false)));

    [Fact]
    public void Second_screen_only_with_lid_open_is_off() =>
        Assert.Equal(LedOffReason.InternalPanelInactive, Eval(Laptop(panel: false)));

    [Fact]
    public void Extend_or_duplicate_including_laptop_panel_is_on() =>
        Assert.Equal(LedOffReason.None, Eval(Laptop(panel: true)));

    [Fact]
    public void Screen_timeout_is_off_and_dim_stays_on()
    {
        Assert.Equal(LedOffReason.DisplayOff, Eval(Laptop(power: DisplayPower.Off)));
        Assert.Equal(LedOffReason.None, Eval(Laptop(power: DisplayPower.Dimmed)));
    }

    [Fact]
    public void Unknown_state_defaults_to_on() => Assert.Equal(LedOffReason.None, Eval(ScreenState.Unknown));

    [Fact]
    public void Lock_only_counts_when_enabled()
    {
        Assert.Equal(LedOffReason.None, Eval(Laptop(locked: true)));
        var opts = new AutoOffOptions { WhenLocked = true };
        Assert.Equal(LedOffReason.Locked, ScreenPolicy.Evaluate(Laptop(locked: true), opts, LedOverride.Auto));
    }

    [Fact]
    public void Manual_override_wins_over_screen_state()
    {
        Assert.Equal(LedOffReason.None, Eval(Laptop(panel: false, lid: false), LedOverride.AlwaysOn));
        Assert.Equal(LedOffReason.ManualOff, Eval(Laptop(), LedOverride.AlwaysOff));
    }

    [Fact]
    public void Suspend_always_turns_off() =>
        Assert.Equal(LedOffReason.Suspending, Eval(Laptop() with { SystemSuspending = true }, LedOverride.AlwaysOn));

    [Fact]
    public void Master_switch_disables_auto_off()
    {
        var opts = new AutoOffOptions { Enabled = false };
        Assert.Equal(LedOffReason.None, ScreenPolicy.Evaluate(Laptop(panel: false, lid: false), opts, LedOverride.Auto));
    }
}
