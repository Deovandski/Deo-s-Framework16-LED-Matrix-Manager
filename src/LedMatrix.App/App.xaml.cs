using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using LedMatrix.App.Interop;
using LedMatrix.App.Services;
using LedMatrix.Core;

namespace LedMatrix.App;

[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Design", "CA1001:Types that own disposable fields should be disposable",
    Justification = "WPF owns the Application lifetime; fields are disposed in Cleanup() on exit.")]
public partial class App : Application
{
    private SingleInstance? _singleInstance;
    private MessageWindow? _messageWindow;
    private AppController? _controller;
    private TrayIcon? _tray;
    private MainWindow? _mainWindow;
    private bool _themeApplied;
    private MenuItem? _autoItem;
    private MenuItem? _onItem;
    private MenuItem? _offItem;
    private MenuItem? _startupItem;
    private bool _exiting;

    public static string VersionText { get; } =
        typeof(App).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    public const string WebsiteUrl = "https://deovandski.github.io/Deo-s-Framework16-LED-Matrix-Manager/";

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstance = new SingleInstance();
        if (!_singleInstance.IsFirstInstance)
        {
            _singleInstance.SignalFirstInstance();
            _singleInstance.Dispose();
            _singleInstance = null;
            Shutdown();
            return;
        }

        FileLogger.Install(); // warnings and errors only, until Debug mode says otherwise
        DispatcherUnhandledException += (_, args) =>
        {
            Log.Error("Unhandled UI exception", args.Exception);
            args.Handled = true; // keep running (NFR-06)
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Log.Error("Unhandled exception", args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Error("Unobserved task exception", args.Exception);
            args.SetObserved();
        };

        // The Fluent theme is loaded on first use (window or tray menu), not here: an app that
        // starts with Windows and sits in the tray shouldn't pay for it until it shows something.
        _messageWindow = new MessageWindow();
        _controller = new AppController(_messageWindow, Dispatcher);
        _controller.StateChanged += (_, _) => UpdateTray();

        _tray = new TrayIcon(_messageWindow) { Menu = BuildTrayMenu() };
        _tray.Activated += (_, _) => ShowMainWindow();
        _tray.MenuOpening += (_, _) =>
        {
            EnsureTheme();
            RefreshTrayMenu();
        };
        UpdateTray();

        _singleInstance.ListenForShowRequests(() => Dispatcher.InvokeAsync(ShowMainWindow));

        var background = e.Args.Contains(StartupRegistration.BackgroundArg, StringComparer.OrdinalIgnoreCase);
        if (!background)
        {
            ShowMainWindow();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Cleanup();
        base.OnExit(e);
    }

    /// <summary>Windows 11 Fluent look, following the system light/dark setting and accent color.</summary>
    private void EnsureTheme()
    {
        if (!_themeApplied)
        {
            _themeApplied = true;
            ThemeMode = ThemeMode.System;
        }
    }

    public void ShowMainWindow()
    {
        if (_controller is null || _exiting)
        {
            return;
        }

        if (_mainWindow is null)
        {
            // The window is created on demand and fully closed when dismissed, so the
            // app uses very little memory while it sits in the tray.
            EnsureTheme();
            _mainWindow = new MainWindow(_controller);
            _mainWindow.Closed += (_, _) =>
            {
                _mainWindow = null;

                // Hand the window's memory back once the UI is idle, instead of waiting for a
                // full collection that a quiet tray app may not trigger for hours.
                Dispatcher.InvokeAsync(() => GC.Collect(2, GCCollectionMode.Optimized), DispatcherPriority.ApplicationIdle);
            };
        }

        _mainWindow.Show();
        if (_mainWindow.WindowState == WindowState.Minimized)
        {
            _mainWindow.WindowState = WindowState.Normal;
        }

        _mainWindow.Activate();
    }

    public void ExitApp()
    {
        _exiting = true;
        _mainWindow?.Close();
        Cleanup();
        Shutdown();
    }

    private void Cleanup()
    {
        _tray?.Dispose();
        _tray = null;
        _controller?.Dispose();
        _controller = null;
        _messageWindow?.Dispose();
        _messageWindow = null;
        _singleInstance?.Dispose();
        _singleInstance = null;
    }

    private ContextMenu BuildTrayMenu()
    {
        var menu = new ContextMenu();

        var open = new MenuItem { Header = "Open LED Matrix Manager", FontWeight = FontWeights.SemiBold };
        open.Click += (_, _) => ShowMainWindow();
        menu.Items.Add(open);
        menu.Items.Add(new Separator());

        _autoItem = new MenuItem { Header = "Automatic (follow laptop screen)", IsCheckable = true };
        _autoItem.Click += (_, _) => SetOverride(LedOverride.Auto);
        _onItem = new MenuItem { Header = "Keep LEDs on", IsCheckable = true };
        _onItem.Click += (_, _) => SetOverride(LedOverride.AlwaysOn);
        _offItem = new MenuItem { Header = "Keep LEDs off", IsCheckable = true };
        _offItem.Click += (_, _) => SetOverride(LedOverride.AlwaysOff);
        menu.Items.Add(_autoItem);
        menu.Items.Add(_onItem);
        menu.Items.Add(_offItem);
        menu.Items.Add(new Separator());

        var managed = StartupRegistration.ManagedByWindows;
        var startupItem = new MenuItem { Header = managed ? "Startup settings…" : "Start with Windows", IsCheckable = !managed };
        startupItem.Click += (_, _) =>
        {
            if (managed)
            {
                StartupRegistration.OpenWindowsStartupSettings();
                return;
            }

            StartupRegistration.SetEnabled(startupItem.IsChecked);
            RefreshTrayMenu();
        };
        _startupItem = startupItem;
        menu.Items.Add(startupItem);
        menu.Items.Add(new Separator());

        var exit = new MenuItem { Header = "Exit" };
        exit.Click += (_, _) => ExitApp();
        menu.Items.Add(exit);

        return menu;
    }

    private void SetOverride(LedOverride value)
    {
        _controller?.SetOverride(value);
        RefreshTrayMenu();
    }

    private void RefreshTrayMenu()
    {
        if (_controller is null)
        {
            return;
        }

        var mode = _controller.Settings.Override;
        _autoItem!.IsChecked = mode == LedOverride.Auto;
        _onItem!.IsChecked = mode == LedOverride.AlwaysOn;
        _offItem!.IsChecked = mode == LedOverride.AlwaysOff;
        _startupItem!.IsChecked = !StartupRegistration.ManagedByWindows && StartupRegistration.IsEnabled();
    }

    private void UpdateTray()
    {
        if (_tray is null || _controller is null)
        {
            return;
        }

        var count = (_controller.Devices.Get(MatrixSide.Left) is null ? 0 : 1) +
                    (_controller.Devices.Get(MatrixSide.Right) is null ? 0 : 1);
        var modules = count switch
        {
            0 => "no modules found",
            1 => "1 module",
            _ => $"{count} modules",
        };
        _tray.Update(_controller.LedsOn, $"LED Matrix Manager\nLEDs: {ScreenPolicy.Describe(_controller.OffReason)}\n{modules}");
    }
}
