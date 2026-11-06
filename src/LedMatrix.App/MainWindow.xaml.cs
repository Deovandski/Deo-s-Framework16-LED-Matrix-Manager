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
using LedMatrix.Core.Animation;
using Frame = LedMatrix.Core.Frame;

namespace LedMatrix.App;

public partial class MainWindow : Window
{
    private static readonly Brush OnBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x3C, 0xC3, 0x6B)));
    private static readonly Brush OffBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x8A, 0x8A, 0x8A)));

    private readonly AppController _controller;
    private bool _loading = true; // suppresses change handlers while the UI is being filled in

    internal MainWindow(AppController controller)
    {
        _controller = controller;
        InitializeComponent();

        LeftGrid.Frame = _controller.Settings.GetFrame(MatrixSide.Left);
        RightGrid.Frame = _controller.Settings.GetFrame(MatrixSide.Right);
        LeftGrid.FrameEdited += (_, _) => OnGridEdited(MatrixSide.Left);
        RightGrid.FrameEdited += (_, _) => OnGridEdited(MatrixSide.Right);

        foreach (var panel in new[] { LeftPanel, RightPanel })
        {
            panel.ContentChanged += OnPanelContentChanged;
            panel.PatternChosen += (sender, pattern) => ApplyDrawing(((SideContentPanel)sender!).Side, pattern.Create());
            panel.ClearRequested += (sender, _) => ApplyDrawing(((SideContentPanel)sender!).Side, new Frame());
            panel.InvertRequested += (sender, _) =>
            {
                var side = ((SideContentPanel)sender!).Side;
                var frame = _controller.Settings.GetFrame(side);
                frame.Invert();
                ApplyDrawing(side, frame);
            };
        }

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
        if (StartupRegistration.ManagedByWindows)
        {
            StartupCheck.Visibility = Visibility.Collapsed;
            StartupManagedText.Visibility = Visibility.Visible;
        }

        CrtTransitionsCheck.IsChecked = s.CrtTransitions;
        DiagnosticsCheck.IsChecked = s.DiagnosticsLog;
        UpdateDiagnosticsText();
        GapCheck.IsChecked = s.KeyboardGap;
        VersionRun.Text = $"Version {App.VersionText}";

        // A cheeky hint on PCs that can't have LED Matrix modules (nothing when unsure).
        if (HardwareInfo.BannerMessage is { } banner && !_controller.HardwareBannerClosed)
        {
            HardwareBannerText.Text = banner;
            HardwareBanner.Visibility = Visibility.Visible;
        }

        _controller.StateChanged += OnStateChanged;
        _controller.PreviewFrame += OnPreviewFrame;
        Closed += (_, _) =>
        {
            _controller.StateChanged -= OnStateChanged;
            _controller.PreviewFrame -= OnPreviewFrame;
            _controller.PreviewEnabled = false;
        };

        RefreshContent();
        RefreshState();
        _loading = false;
        // Grids animate only while the window can be seen: not when minimized or hidden.
        StateChanged += (_, _) => UpdatePreviewEnabled();
        IsVisibleChanged += (_, _) => UpdatePreviewEnabled();
        _controller.PreviewEnabled = true;
    }

    private static string Percent(double value) => $"{Math.Round(value / 255 * 100)}%";

    private static MatrixSide SideOf(object sender) =>
        Enum.Parse<MatrixSide>((string)((FrameworkElement)sender).Tag);

    private LedGrid GridOf(MatrixSide side) => side == MatrixSide.Left ? LeftGrid : RightGrid;

    private static Brush Frozen(Brush brush)
    {
        brush.Freeze();
        return brush;
    }

    private void UpdatePreviewEnabled() =>
        _controller.PreviewEnabled = IsVisible && WindowState != WindowState.Minimized;

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

    // ----- Content (drawing / animation / text) -----

    /// <summary>Reloads both content panels and decides which grids can be drawn on.</summary>
    private void RefreshContent()
    {
        var wasLoading = _loading;
        _loading = true;
        try
        {
            RefreshContentCore();
        }
        finally
        {
            _loading = wasLoading;
        }
    }

    private void RefreshContentCore()
    {
        var s = _controller.Settings;
        LinkIndependent.IsChecked = s.Link == LinkMode.Independent;
        LinkMirror.IsChecked = s.Link == LinkMode.Mirror;
        LinkSpan.IsChecked = s.Link == LinkMode.Span;
        GapCheck.IsEnabled = s.Link == LinkMode.Span;

        LeftPanel.Load(s.LeftContent, s.Link, following: false);
        RightPanel.Load(s.RightContent, s.Link, following: RightFollowsLeft());
        UpdateEditability();
    }

    /// <summary>In Mirror and Span the right module shows the left content (except drawings in Span, which stay per side).</summary>
    private bool RightFollowsLeft()
    {
        var s = _controller.Settings;
        return s.Link == LinkMode.Mirror || (s.Link == LinkMode.Span && s.LeftContent.Kind != ContentKind.Drawing);
    }

    /// <summary>A grid is editable when it shows a plain drawing; otherwise it is a live preview.</summary>
    private void UpdateEditability()
    {
        var s = _controller.Settings;
        var leftEditable = s.LeftContent.Kind == ContentKind.Drawing && !s.LeftContent.CrtLook;
        var rightEditable = s.Link switch
        {
            LinkMode.Independent => s.RightContent.Kind == ContentKind.Drawing && !s.RightContent.CrtLook,
            LinkMode.Span => leftEditable, // Span with drawings: each side keeps its own drawing
            _ => false,
        };
        SetEditable(MatrixSide.Left, leftEditable);
        SetEditable(MatrixSide.Right, rightEditable);
    }

    private void SetEditable(MatrixSide side, bool editable)
    {
        var grid = GridOf(side);
        var wasReadOnly = grid.IsReadOnly;
        grid.IsReadOnly = !editable;
        if (editable && wasReadOnly)
        {
            grid.Frame = _controller.Settings.GetFrame(side); // back to the drawing
        }
    }

    private void OnPanelContentChanged(object? sender, Action<SideContent> change)
    {
        var panel = (SideContentPanel)sender!;
        var before = _controller.Settings.LeftContent.Kind;
        _controller.UpdateContent(panel.Side, change);

        // The right panel depends on the left content in Mirror/Span; reload it, never the one being typed in.
        if (panel.Side == MatrixSide.Left && (_controller.Settings.Link != LinkMode.Independent || before != _controller.Settings.LeftContent.Kind))
        {
            RightPanel.Load(_controller.Settings.RightContent, _controller.Settings.Link, following: RightFollowsLeft());
        }

        UpdateEditability();
    }

    private void OnPreviewFrame(MatrixSide side, Frame frame)
    {
        var grid = GridOf(side);
        if (grid.IsReadOnly)
        {
            grid.Frame = frame;
        }
    }

    private void OnGridEdited(MatrixSide side) => _controller.SetDrawing(side, GridOf(side).Frame);

    private void ApplyDrawing(MatrixSide side, Frame frame)
    {
        _controller.SetDrawing(side, frame);
        if (!GridOf(side).IsReadOnly)
        {
            GridOf(side).Frame = frame;
        }
    }

    private void Link_Checked(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        var link = LinkMirror.IsChecked == true ? LinkMode.Mirror
            : LinkSpan.IsChecked == true ? LinkMode.Span
            : LinkMode.Independent;
        _controller.SetLink(link);
        RefreshContent();
    }

    private void Gap_Click(object sender, RoutedEventArgs e) => _controller.SetKeyboardGap(GapCheck.IsChecked == true);

    private void CrtTransitions_Click(object sender, RoutedEventArgs e) =>
        _controller.SetCrtTransitions(CrtTransitionsCheck.IsChecked == true);

    private void Diagnostics_Click(object sender, RoutedEventArgs e)
    {
        _controller.SetDiagnosticsLog(DiagnosticsCheck.IsChecked == true);
        UpdateDiagnosticsText();
    }

    private void UpdateDiagnosticsText()
    {
        var on = _controller.Diagnostics.Enabled;
        DiagnosticsText.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        DiagnosticsFileRun.Text = Path.GetFileName(DiagnosticsLog.TodayFile);
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

    private void StartupSettings_Click(object sender, RoutedEventArgs e) => StartupRegistration.OpenWindowsStartupSettings();

    // ----- Hardware banner -----

    private void HardwareBannerClose_Click(object sender, RoutedEventArgs e)
    {
        _controller.HardwareBannerClosed = true; // until the app exits
        HardwareBanner.Visibility = Visibility.Collapsed;
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
        OpenShell(PackageInfo.ForExplorer(FileLogger.LogDirectory));
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
