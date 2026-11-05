using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using LedMatrix.App.Interop;
using LedMatrix.Core;
using Microsoft.Win32;
using static LedMatrix.App.Interop.NativeMethods;

namespace LedMatrix.App.Services;

/// <summary>
/// Minimal notification-area icon built directly on Shell_NotifyIcon, with a WPF (Fluent-styled)
/// context menu. Picks a light/dark glyph to match the taskbar and re-adds itself if Explorer restarts.
/// </summary>
internal sealed class TrayIcon : IDisposable
{
    private const uint IconId = 1;
    private const int CallbackMessage = WM_APP + 1;

    private readonly MessageWindow _window;
    private readonly uint _taskbarCreatedMessage;
    private IntPtr _hIcon;
    private bool _added;
    private bool _lit = true;
    private string _tooltip = "Deo's LED Matrix Manager";

    public TrayIcon(MessageWindow window)
    {
        _window = window;
        _taskbarCreatedMessage = RegisterWindowMessage("TaskbarCreated");
        _window.Message += OnMessage;
        LoadIcon();
        Add();
    }

    /// <summary>Left click / keyboard select on the icon.</summary>
    public event EventHandler? Activated;

    /// <summary>Called right before the menu opens, so callers can refresh check marks.</summary>
    public event EventHandler? MenuOpening;

    public ContextMenu? Menu { get; set; }

    public void Update(bool lit, string tooltip)
    {
        var iconChanged = lit != _lit;
        _lit = lit;
        _tooltip = tooltip.Length > 127 ? tooltip[..127] : tooltip;
        if (iconChanged)
        {
            LoadIcon();
        }

        Modify();
    }

    public void Dispose()
    {
        _window.Message -= OnMessage;
        if (_added)
        {
            var data = NewData(0);
            Shell_NotifyIcon(NIM_DELETE, ref data);
            _added = false;
        }

        if (_hIcon != IntPtr.Zero)
        {
            DestroyIcon(_hIcon);
            _hIcon = IntPtr.Zero;
        }
    }

    private NOTIFYICONDATAW NewData(uint flags) => new()
    {
        cbSize = Marshal.SizeOf<NOTIFYICONDATAW>(),
        hWnd = _window.Handle,
        uID = IconId,
        uFlags = flags,
        uCallbackMessage = CallbackMessage,
        hIcon = _hIcon,
        szTip = _tooltip,
        szInfo = string.Empty,
        szInfoTitle = string.Empty,
    };

    private void Add()
    {
        var data = NewData(NIF_MESSAGE | NIF_ICON | NIF_TIP | NIF_SHOWTIP);
        _added = Shell_NotifyIcon(NIM_ADD, ref data);
        if (!_added)
        {
            Log.Warn("Shell_NotifyIcon(NIM_ADD) failed");
            return;
        }

        data.uVersion = NOTIFYICON_VERSION_4;
        Shell_NotifyIcon(NIM_SETVERSION, ref data);
    }

    private void Modify()
    {
        if (!_added)
        {
            return;
        }

        var data = NewData(NIF_ICON | NIF_TIP | NIF_SHOWTIP);
        Shell_NotifyIcon(NIM_MODIFY, ref data);
    }

    private void LoadIcon()
    {
        var theme = TaskbarUsesLightTheme() ? "light" : "dark";
        var state = _lit ? "on" : "off";
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", $"tray-{theme}-{state}.ico");
        var size = GetSystemMetrics(SM_CXSMICON);
        var newIcon = LoadImage(IntPtr.Zero, path, IMAGE_ICON, size, size, LR_LOADFROMFILE);
        if (newIcon == IntPtr.Zero)
        {
            Log.Warn($"Could not load tray icon {path}");
            return;
        }

        var old = _hIcon;
        _hIcon = newIcon;
        if (old != IntPtr.Zero)
        {
            DestroyIcon(old);
        }
    }

    private static bool TaskbarUsesLightTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("SystemUsesLightTheme") is int v && v != 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private bool OnMessage(int msg, IntPtr wParam, IntPtr lParam)
    {
        if (_taskbarCreatedMessage != 0 && msg == (int)_taskbarCreatedMessage)
        {
            // Explorer restarted: the icon is gone and must be added again.
            _added = false;
            Add();
            return false;
        }

        if (msg == WM_SETTINGCHANGE)
        {
            // Taskbar light/dark may have changed ("ImmersiveColorSet").
            LoadIcon();
            Modify();
            return false;
        }

        if (msg != CallbackMessage)
        {
            return false;
        }

        // NOTIFYICON_VERSION_4: LOWORD(lParam) = event.
        switch (LoWord(lParam))
        {
            case NIN_SELECT:
            case NIN_KEYSELECT:
            case WM_LBUTTONDBLCLK:
                Activated?.Invoke(this, EventArgs.Empty);
                return true;

            case WM_CONTEXTMENU: // right click or Shift+F10 / Menu key
                ShowMenu();
                return true;
        }

        return false;
    }

    private void ShowMenu()
    {
        if (Menu is null)
        {
            return;
        }

        MenuOpening?.Invoke(this, EventArgs.Empty);

        // Needed so the menu closes when the user clicks elsewhere.
        SetForegroundWindow(_window.Handle);
        Menu.Placement = PlacementMode.MousePoint;
        Menu.IsOpen = true;
    }
}
