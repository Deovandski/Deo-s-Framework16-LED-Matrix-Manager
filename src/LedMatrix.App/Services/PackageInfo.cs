using System.IO;
using LedMatrix.App.Interop;
using LedMatrix.Core;

namespace LedMatrix.App.Services;

/// <summary>
/// Whether this copy runs as the Microsoft Store (MSIX) package. The Store copy lets Windows handle
/// startup and updates, and Windows keeps its %LocalAppData% files in a private per-package folder.
/// </summary>
internal static class PackageInfo
{
    private static readonly Lazy<string?> Family = new(ReadFamilyName);

    /// <summary>True when installed from the Microsoft Store (or a test MSIX).</summary>
    public static bool IsPackaged => Family.Value is not null;

    /// <summary>The package family name, or null when not packaged.</summary>
    public static string? FamilyName => Family.Value;

    /// <summary>
    /// The folder as Explorer sees it. A packaged app's writes under %LocalAppData% really land in
    /// %LocalAppData%\Packages\&lt;family&gt;\LocalCache\Local\…, which Explorer (outside the package) needs.
    /// </summary>
    public static string ForExplorer(string path)
    {
        if (FamilyName is not { } family)
        {
            return path;
        }

        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var relative = Path.GetRelativePath(local, path);
        if (relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative))
        {
            return path;
        }

        var redirected = Path.Combine(local, "Packages", family, "LocalCache", "Local", relative);
        return Directory.Exists(redirected) ? redirected : path;
    }

    private static string? ReadFamilyName()
    {
        try
        {
            uint length = 0;
            var rc = NativeMethods.GetCurrentPackageFamilyName(ref length, null);
            if (rc == NativeMethods.APPMODEL_ERROR_NO_PACKAGE || rc != NativeMethods.ERROR_INSUFFICIENT_BUFFER || length == 0)
            {
                return null;
            }

            var buffer = new char[length];
            rc = NativeMethods.GetCurrentPackageFamilyName(ref length, buffer);
            return rc == 0 ? new string(buffer, 0, (int)Math.Max(0, length - 1)) : null;
        }
        catch (Exception ex) when (ex is EntryPointNotFoundException or DllNotFoundException)
        {
            Log.Debug("Package identity API not available");
            return null;
        }
    }
}
