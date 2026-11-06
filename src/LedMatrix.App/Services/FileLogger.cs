using System.Globalization;
using System.IO;
using System.Text;
using LedMatrix.Core;

namespace LedMatrix.App.Services;

/// <summary>
/// Rolling text log in %LocalAppData%\DeoLedMatrixManager\logs (5 x 1 MB). Normally only warnings
/// and errors are written (nothing at all on a good day); Debug mode in the window writes everything.
/// The folder is only created when there's something to write.
/// </summary>
internal static class FileLogger
{
    private const long MaxBytes = 1024 * 1024;
    private const int MaxFiles = 5;
    private static readonly object Gate = new();
    private static string? _path;

    public static string LogDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DeoLedMatrixManager", "logs");

    public static void Install()
    {
        _path = Path.Combine(LogDirectory, "app.log");
        Log.MinimumLevel = LogLevel.Warn; // until settings say Debug mode is on
        Log.Sink = Write;
    }

    /// <summary>Debug mode: log everything (true) or only warnings and errors (false).</summary>
    public static void SetDetailed(bool detailed) => Log.MinimumLevel = detailed ? LogLevel.Debug : LogLevel.Warn;

    private static void Write(LogLevel level, string message)
    {
        if (_path is null)
        {
            return;
        }

        var line = string.Create(CultureInfo.InvariantCulture, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}");
        lock (Gate)
        {
            try
            {
                Directory.CreateDirectory(LogDirectory);
                var info = new FileInfo(_path);
                if (info.Exists && info.Length > MaxBytes)
                {
                    Roll();
                }

                File.AppendAllText(_path, line, Encoding.UTF8);
            }
            catch (IOException)
            {
                // Never let logging break the app.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        System.Diagnostics.Debug.Write(line);
    }

    private static void Roll()
    {
        for (var i = MaxFiles - 1; i >= 1; i--)
        {
            var src = Path.Combine(LogDirectory, i == 1 ? "app.log" : $"app.{i - 1}.log");
            var dst = Path.Combine(LogDirectory, $"app.{i}.log");
            if (File.Exists(src))
            {
                File.Move(src, dst, overwrite: true);
            }
        }
    }
}
