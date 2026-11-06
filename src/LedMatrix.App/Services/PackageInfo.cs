using System.IO;
using System.Runtime.InteropServices;
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
    private static readonly Lazy<bool> SignIn = new(ReadStartedBySignIn);

    /// <summary>True when installed from the Microsoft Store (or a test MSIX).</summary>
    public static bool IsPackaged => Family.Value is not null;

    /// <summary>The package family name, or null when not packaged.</summary>
    public static string? FamilyName => Family.Value;

    /// <summary>
    /// True when the package's startup task started the app at sign-in. Startup tasks can't pass
    /// "--background", so the app asks Windows instead. Windows answers only once per process,
    /// hence the cache. Always false when not packaged.
    /// </summary>
    public static bool StartedAtSignIn => SignIn.Value;

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

    private static bool ReadStartedBySignIn()
    {
        if (!IsPackaged)
        {
            return false;
        }

        const string ClassName = "Windows.ApplicationModel.AppInstance";
        var name = IntPtr.Zero;
        NativeMethods.IAppInstanceStatics? statics = null;
        NativeMethods.IActivatedEventArgs? args = null;
        try
        {
            if (NativeMethods.WindowsCreateString(ClassName, ClassName.Length, out name) < 0)
            {
                return false;
            }

            var iid = typeof(NativeMethods.IAppInstanceStatics).GUID;
            var hr = NativeMethods.RoGetActivationFactory(name, ref iid, out statics);
            if (hr < 0 || statics is null)
            {
                Log.Debug($"AppInstance not available (0x{hr:X8})");
                return false;
            }

            args = statics.GetActivatedEventArgs();
            var kind = args?.GetKind();
            Log.Debug($"Started by activation kind {kind?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "none"}");
            return kind == NativeMethods.ActivationKind_StartupTask;
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or EntryPointNotFoundException or DllNotFoundException)
        {
            Log.Debug($"Could not read how the app was started: {ex.Message}");
            return false;
        }
        finally
        {
            if (args is not null)
            {
                Marshal.ReleaseComObject(args);
            }

            if (statics is not null)
            {
                Marshal.ReleaseComObject(statics);
            }

            if (name != IntPtr.Zero)
            {
                _ = NativeMethods.WindowsDeleteString(name);
            }
        }
    }
}
