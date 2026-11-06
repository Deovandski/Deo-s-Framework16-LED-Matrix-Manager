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

    /// <summary>True when the only tokens are {time} and {date}: the text then changes once a minute at most.</summary>
    public static bool OnlyClockTokens(string text)
    {
        if (!HasTokens(text))
        {
            return false;
        }

        var rest = text.Replace("{time}", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("{date}", string.Empty, StringComparison.OrdinalIgnoreCase);
        return !Has(rest, "{battery}") && !Has(rest, "{cpu}") && !Has(rest, "{ram}");
    }

    public static string Expand(string text, DateTimeOffset now, ISystemStats? stats)
    {
        if (!HasTokens(text))
        {
            return text;
        }

        // Each value is only looked up when its token is used (the stats are system calls).
        static string Pct(int? v) => v is { } p ? p.ToString(CultureInfo.InvariantCulture) + "%" : "--";

        if (Has(text, "{time}"))
        {
            text = text.Replace("{time}", now.ToString("HH:mm", CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase);
        }

        if (Has(text, "{date}"))
        {
            text = text.Replace("{date}", $"{Months[now.Month - 1]} {now.Day.ToString(CultureInfo.InvariantCulture)}", StringComparison.OrdinalIgnoreCase);
        }

        if (Has(text, "{battery}"))
        {
            text = text.Replace("{battery}", Pct(stats?.BatteryPercent), StringComparison.OrdinalIgnoreCase);
        }

        if (Has(text, "{cpu}"))
        {
            text = text.Replace("{cpu}", Pct(stats?.CpuPercent), StringComparison.OrdinalIgnoreCase);
        }

        if (Has(text, "{ram}"))
        {
            text = text.Replace("{ram}", Pct(stats?.RamPercent), StringComparison.OrdinalIgnoreCase);
        }

        return text;
    }

    private static bool Has(string text, string token) => text.Contains(token, StringComparison.OrdinalIgnoreCase);
}
