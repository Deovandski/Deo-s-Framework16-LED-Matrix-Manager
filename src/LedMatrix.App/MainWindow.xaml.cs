using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Navigation;
using LedMatrix.App.Controls;
using LedMatrix.App.Services;
using LedMatrix.Core;
using Frame = LedMatrix.Core.Frame;

namespace LedMatrix.App;

public partial class MainWindow : Window
{
    private const string CustomItem = "Custom drawing";
    private static readonly Brush OnBrush = new SolidColorBrush(Color.FromRgb(0x3C, 0xC3, 0x6B));
    private static readonly Brush OffBrush = new SolidColorBrush(Color.FromRgb(0x8A, 0x8A, 0x8A));

    private readonly AppController _controller;
    private bool _loading = true; // suppresses change handlers while the UI is being filled in

    internal MainWindow(AppController controller)
    {
        _controller = controller;
        InitializeComponent();

        foreach (var combo in new[] { LeftPattern, RightPattern })
        {
            combo.Items.Add(CustomItem);
            foreach (var p in Patterns.All)
            {
                combo.Items.Add(p.Name);
            }

            combo.SelectedIndex = 0;
        }

        LeftGrid.Frame = _controller.Devices.GetFrame(MatrixSide.Left);
        RightGrid.Frame = _controller.Devices.GetFrame(MatrixSide.Right);
        LeftGrid.FrameEdited += (_, _) => OnGridEdited(MatrixSide.Left);
        RightGrid.FrameEdited += (_, _) => OnGridEdited(MatrixSide.Right);

        var s = _controller.Settings;
        BrightnessSlider.Value = s.Brightness;
        BrightnessValue.Text = Percent(s.Brightness);
        BrushSlider.Value = s.BrushBrightness;
        BrushValue.Text = Percent(s.BrushBrightness);
        LeftGrid.BrushValue = RightGrid.BrushValue = s.BrushBrightness;

        AutoOffCheck.IsChecked = s.AutoOff.Enabled;
        LidCheck.IsChecked = s.AutoOff.WhenLidClosed;
        PanelCheck.IsChecked = s.AutoOff.WhenInternalPanelInactive;
        DisplayOffCheck.IsChecked = s.AutoOff.WhenDisplayOff;
        LockCheck.IsChecked = s.AutoOff.WhenLocked;
        AutoOffDetails.IsEnabled = s.AutoOff.Enabled;
        StartupCheck.IsChecked = StartupRegistration.IsEnabled();
        VersionRun.Text = $"Version {App.VersionText}";

        _controller.StateChanged += OnStateChanged;
        Closed += (_, _) => _controller.StateChanged -= OnStateChanged;

        RefreshState();
        _loading = false;
    }

    private static string Percent(double value) => $"{Math.Round(value / 255 * 100)}%";

    private static MatrixSide SideOf(object sender) =>
        Enum.Parse<MatrixSide>((string)((FrameworkElement)sender).Tag);

    private LedGrid GridOf(MatrixSide side) => side == MatrixSide.Left ? LeftGrid : RightGrid;

    private ComboBox PatternBoxOf(MatrixSide side) => side == MatrixSide.Left ? LeftPattern : RightPattern;

    private void OnStateChanged(object? sender, EventArgs e) => RefreshState();

    private void RefreshState()
    {
        var wasLoading = _loading;
        _loading = true;
        try
        {
            StateText.Text = $"LEDs: {ScreenPolicy.Describe(_controller.OffReason)}";
            StateDot.Fill = _controller.LedsOn ? OnBrush : OffBrush;

            RefreshDevice(MatrixSide.Left, LeftNameBox, LeftStatusText, LeftIdentifyButton, LeftHeader);
            RefreshDevice(MatrixSide.Right, RightNameBox, RightStatusText, RightIdentifyButton, RightHeader);
            SwapButton.IsEnabled = _controller.Devices.Get(MatrixSide.Left) is not null ||
                                   _controller.Devices.Get(MatrixSide.Right) is not null;

            var mode = _controller.Settings.Override;
            ModeAuto.IsChecked = mode == LedOverride.Auto;
            ModeOn.IsChecked = mode == LedOverride.AlwaysOn;
            ModeOff.IsChecked = mode == LedOverride.AlwaysOff;

            var st = _controller.Screen.Current;
            ScreenStateText.Text =
                $"Laptop screen: {YesNo(st.InternalPanelActive, "in use", "not in use")} · " +
                $"Lid: {YesNo(st.LidOpen, "open", "closed")} · " +
                $"Display: {(st.DisplayPower == DisplayPower.Unknown ? "unknown" : st.DisplayPower.ToString().ToLowerInvariant())}";
        }
        finally
        {
            _loading = wasLoading;
        }
    }

    private static string YesNo(bool? value, string yes, string no) => value switch
    {
        true => yes,
        false => no,
        null => "unknown",
    };

    private void RefreshDevice(MatrixSide side, TextBox nameBox, TextBlock status, Button identify, TextBlock header)
    {
        var device = _controller.Devices.Get(side);
        var name = _controller.DeviceName(side);
        header.Text = name;
        if (!nameBox.IsKeyboardFocusWithin)
        {
            nameBox.Text = name;
        }

        nameBox.IsEnabled = device is not null;
        identify.IsEnabled = device is not null;
        status.Text = device is null
            ? "Not detected"
            : $"{device.PortName} · firmware {device.Version?.ToString() ?? "…"} · {StatusText(device.Status)}";
    }

    private static string StatusText(DeviceStatus status) => status switch
    {
        DeviceStatus.Connected => "connected",
        DeviceStatus.Asleep => "LEDs off",
        DeviceStatus.PortBusy => "in use by another app (close other LED tools)",
        DeviceStatus.Disconnected => "disconnected, retrying…",
        _ => "connecting…",
    };

    // ----- Drawing -----

    private void OnGridEdited(MatrixSide side)
    {
        _controller.SetFrame(side, GridOf(side).Frame);
        var combo = PatternBoxOf(side);
        if (combo.SelectedIndex != 0)
        {
            _loading = true;
            combo.SelectedIndex = 0;
            _loading = false;
        }
    }

    private void Pattern_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || sender is not ComboBox combo || combo.SelectedItem is not string name || name == CustomItem)
        {
            return;
        }

        var pattern = Patterns.Find(name);
        if (pattern is null)
        {
            return;
        }

        var side = SideOf(sender);
        var frame = pattern.Create();
        GridOf(side).Frame = frame;
        _controller.SetFrame(side, frame);
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        var side = SideOf(sender);
        GridOf(side).Frame = new Frame();
        OnGridEdited(side);
    }

    private void Invert_Click(object sender, RoutedEventArgs e)
    {
        var side = SideOf(sender);
        var frame = GridOf(side).Frame.Clone();
        frame.Invert();
        GridOf(side).Frame = frame;
        OnGridEdited(side);
    }

    private void Brush_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading)
        {
            return;
        }

        var value = (byte)Math.Clamp(Math.Round(e.NewValue), 1, 255);
        BrushValue.Text = Percent(value);
        LeftGrid.BrushValue = RightGrid.BrushValue = value;
        _controller.SetBrushBrightness(value);
    }

    // ----- LEDs -----

    private void Brightness_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading)
        {
            return;
        }

        var value = (byte)Math.Clamp(Math.Round(e.NewValue), 0, 255);
        BrightnessValue.Text = Percent(value);
        _controller.SetBrightness(value);
    }

    private void Mode_Checked(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        var mode = ModeOn.IsChecked == true ? LedOverride.AlwaysOn
            : ModeOff.IsChecked == true ? LedOverride.AlwaysOff
            : LedOverride.Auto;
        _controller.SetOverride(mode);
    }

    // ----- Devices -----

    private void Identify_Click(object sender, RoutedEventArgs e) => _controller.Devices.Identify(SideOf(sender));

    private void Swap_Click(object sender, RoutedEventArgs e) => _controller.Swap();

    private void Rescan_Click(object sender, RoutedEventArgs e) => _controller.Devices.Rescan();

    private void NameBox_LostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is TextBox box)
        {
            _controller.RenameDevice(SideOf(sender), box.Text);
        }
    }

    private void NameBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is TextBox box)
        {
            _controller.RenameDevice(SideOf(sender), box.Text);
            Keyboard.ClearFocus();
            e.Handled = true;
        }
    }

    // ----- Automatic on/off & startup -----

    private void AutoOff_Click(object sender, RoutedEventArgs e)
    {
        var o = _controller.Settings.AutoOff;
        o.Enabled = AutoOffCheck.IsChecked == true;
        o.WhenLidClosed = LidCheck.IsChecked == true;
        o.WhenInternalPanelInactive = PanelCheck.IsChecked == true;
        o.WhenDisplayOff = DisplayOffCheck.IsChecked == true;
        o.WhenLocked = LockCheck.IsChecked == true;
        AutoOffDetails.IsEnabled = o.Enabled;
        _controller.AutoOffChanged();
    }

    private void Startup_Click(object sender, RoutedEventArgs e)
    {
        StartupRegistration.SetEnabled(StartupCheck.IsChecked == true);
        StartupCheck.IsChecked = StartupRegistration.IsEnabled();
    }

    // ----- Links -----

    private void Link_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        OpenShell(e.Uri.AbsoluteUri);
        e.Handled = true;
    }

    private void OpenLogs_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(FileLogger.LogDirectory);
        OpenShell(FileLogger.LogDirectory);
    }

    private static void OpenShell(string target)
    {
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not open {target}", ex);
        }
    }
}
