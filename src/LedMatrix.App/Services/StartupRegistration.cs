using LedMatrix.Core;
using Microsoft.Win32;

namespace LedMatrix.App.Services;

/// <summary>
/// "Start with Windows" via the per-user Run key (no admin needed). The installer writes the
/// same value, so the checkbox in the app and the installer option stay in sync.
/// </summary>
internal static class StartupRegistration
{
    public const string ValueName = "DeoLedMatrixManager";
    public const string BackgroundArg = "--background";
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static bool IsEnabled()
    {
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
