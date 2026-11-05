using System.Globalization;
using System.IO;
using System.Text;
using LedMatrix.Core;

namespace LedMatrix.App.Services;

/// <summary>Rolling text log in %LocalAppData%\DeoLedMatrixManager\logs (5 x 1 MB).</summary>
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
        Directory.CreateDirectory(LogDirectory);
        _path = Path.Combine(LogDirectory, "app.log");
#if DEBUG
        var minimum = LogLevel.Debug;
#else
        var minimum = LogLevel.Info;
#endif
        Log.Sink = (level, message) =>
        {
            if (level >= minimum)
            {
                Write(level, message);
            }
        };
    }

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
