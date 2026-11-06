namespace LedMatrix.Core;

public enum LogLevel
{
    Debug,
    Info,
    Warn,
    Error,
}

/// <summary>Tiny logging facade. The app plugs in a rolling file sink; tests can capture messages.</summary>
public static class Log
{
    public static Action<LogLevel, string>? Sink { get; set; }

    /// <summary>Messages below this level are dropped before they reach the sink (the app sets it from Debug mode).</summary>
    public static LogLevel MinimumLevel { get; set; } = LogLevel.Debug;

    public static bool IsEnabled(LogLevel level) => level >= MinimumLevel && Sink is not null;

    public static void Debug(string message) => Write(LogLevel.Debug, message);

    public static void Info(string message) => Write(LogLevel.Info, message);

    public static void Warn(string message, Exception? ex = null) => Write(LogLevel.Warn, Format(message, ex));

    public static void Error(string message, Exception? ex = null) => Write(LogLevel.Error, Format(message, ex));

    private static string Format(string message, Exception? ex) =>
        ex is null ? message : $"{message}: {ex.GetType().Name}: {ex.Message}";

    private static void Write(LogLevel level, string message)
    {
        if (level < MinimumLevel)
        {
            return;
        }

        try
        {
            Sink?.Invoke(level, message);
        }
        catch
        {
            // Logging must never take the app down.
        }
    }
}
