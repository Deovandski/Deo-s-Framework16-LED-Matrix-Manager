using System.Globalization;

namespace LedMatrix.Core.Animation;

/// <summary>Live values the app can provide for text tokens. Null = unavailable (shown as "--").</summary>
public interface ISystemStats
{
    int? BatteryPercent { get; }

    int? CpuPercent { get; }

    int? RamPercent { get; }
}

/// <summary>Expands {time}, {date}, {battery}, {cpu} and {ram} in text.</summary>
public static class TextTokens
{
    private static readonly string[] Months = ["JAN", "FEB", "MAR", "APR", "MAY", "JUN", "JUL", "AUG", "SEP", "OCT", "NOV", "DEC"];

    public static bool HasTokens(string text) => text.Contains('{', StringComparison.Ordinal);

    public static string Expand(string text, DateTimeOffset now, ISystemStats? stats)
    {
        if (!HasTokens(text))
        {
            return text;
        }

        static string Pct(int? v) => v is { } p ? p.ToString(CultureInfo.InvariantCulture) + "%" : "--";

        return text
            .Replace("{time}", now.ToString("HH:mm", CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase)
            .Replace("{date}", $"{Months[now.Month - 1]} {now.Day.ToString(CultureInfo.InvariantCulture)}", StringComparison.OrdinalIgnoreCase)
            .Replace("{battery}", Pct(stats?.BatteryPercent), StringComparison.OrdinalIgnoreCase)
            .Replace("{cpu}", Pct(stats?.CpuPercent), StringComparison.OrdinalIgnoreCase)
            .Replace("{ram}", Pct(stats?.RamPercent), StringComparison.OrdinalIgnoreCase);
    }
}
