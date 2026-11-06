using System.Diagnostics;
using LedMatrix.Core;
using Microsoft.Win32;

namespace LedMatrix.App.Services;

/// <summary>
/// "Start with Windows" via the per-user Run key (no admin needed). The installer writes the
/// same value, so the checkbox in the app and the installer option stay in sync.
/// The Microsoft Store copy can't use the Run key: its package declares a startup task instead,
/// which people switch on or off in Settings &gt; Apps &gt; Startup.
/// </summary>
internal static class StartupRegistration
{
    public const string ValueName = "DeoLedMatrixManager";
    public const string BackgroundArg = "--background";
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    /// <summary>True for the Store copy: Windows owns the startup setting.</summary>
    public static bool ManagedByWindows => PackageInfo.IsPackaged;

    /// <summary>Opens Settings &gt; Apps &gt; Startup.</summary>
    public static void OpenWindowsStartupSettings()
    {
        try
        {
            Process.Start(new ProcessStartInfo("ms-settings:startupapps") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Warn("Could not open the Startup apps settings", ex);
        }
    }

    public static bool IsEnabled()
    {
        if (ManagedByWindows)
        {
            return false;
        }

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string s && !string.IsNullOrWhiteSpace(s);
        }
        catch (Exception ex)
        {
            Log.Warn("Could not read startup registration", ex);
            return false;
        }
    }

    public static void SetEnabled(bool enabled)
    {
        if (ManagedByWindows)
        {
            return;
        }

        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
            if (enabled)
            {
                var exe = Environment.ProcessPath ?? throw new InvalidOperationException("Unknown process path.");
                key.SetValue(ValueName, $"\"{exe}\" {BackgroundArg}");
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }
        }
        catch (Exception ex)
        {
            Log.Error("Could not change startup registration", ex);
        }
    }
}
