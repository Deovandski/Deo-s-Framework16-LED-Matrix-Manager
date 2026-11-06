using System.Text.RegularExpressions;

namespace LedMatrix.Core;

/// <summary>What kind of PC the app is running on, as far as LED Matrix support goes.</summary>
public enum HardwareMatch
{
    /// <summary>Not enough information to tell (missing, blank or placeholder values).</summary>
    Unknown,

    /// <summary>A Framework Laptop 16, the only model with LED Matrix input modules.</summary>
    Framework16,

    /// <summary>A Framework computer that isn't a Laptop 16 (Laptop 12/13, Desktop, ...).</summary>
    OtherFramework,

    /// <summary>A computer from another manufacturer.</summary>
    NotFramework,
}

/// <summary>
/// Classifies the PC from its SMBIOS system values (manufacturer, product name, family), so the
/// main window can show a light-hearted banner on PCs that can't have LED Matrix modules.
/// When in doubt the answer is <see cref="HardwareMatch.Unknown"/>, which shows nothing.
/// </summary>
/// <remarks>
/// Matching rules (all case-insensitive, values trimmed):
/// <list type="bullet">
/// <item>Blank or placeholder values ("To Be Filled By O.E.M.", "System manufacturer", "Default string", ...)
/// count as missing. Custom-built PCs often report these, so they never mean "not a Framework".</item>
/// <item>Framework: the manufacturer contains the whole word "Framework" ("Framework", "Framework Computer Inc."),
/// but not "Frameworks Ltd".</item>
/// <item>Laptop 16: the product name or the family contains "Laptop 16" as whole words, so
/// "Laptop 16 (AMD Ryzen 7040 Series)" matches but "Laptop 160" and "Laptop 13" don't.</item>
/// <item>A Framework with no usable product name is Unknown, unless the family says Laptop 16.</item>
/// </list>
/// </remarks>
public static partial class HardwareCheck
{
    /// <summary>Banner for PCs from other manufacturers.</summary>
    public const string NotFrameworkMessage =
        "Looks like you don't have a Framework... Why not buy one and support a great company?";

    /// <summary>Banner for Framework PCs that aren't a Laptop 16.</summary>
    public const string OtherFrameworkMessage =
        "Hey there! You have a Framework, but not the one this is meant for... Why not upgrade ;)";

    // Values firmware vendors leave in when the board maker didn't fill them in.
    private static readonly HashSet<string> Placeholders = new(StringComparer.OrdinalIgnoreCase)
    {
        "To Be Filled By O.E.M.",
        "To Be Filled By OEM",
        "System manufacturer",
        "System Product Name",
        "System Version",
        "Default string",
        "Not Applicable",
        "Not Specified",
        "N/A",
        "None",
        "Unknown",
        "Undefined",
        "OEM",
        "O.E.M.",
    };

    /// <summary>Classifies the PC from the SMBIOS system manufacturer, product name and family.</summary>
    public static HardwareMatch Classify(string? manufacturer, string? productName, string? family)
    {
        var maker = Clean(manufacturer);
        if (maker is null)
        {
            return HardwareMatch.Unknown;
        }

        if (!FrameworkWord().IsMatch(maker))
        {
            return HardwareMatch.NotFramework;
        }

        var product = Clean(productName);
        if (IsLaptop16(product) || IsLaptop16(Clean(family)))
        {
            return HardwareMatch.Framework16;
        }

        // Without a product name we can't tell which Framework this is.
        return product is null ? HardwareMatch.Unknown : HardwareMatch.OtherFramework;
    }

    /// <summary>The banner text for a match, or null when nothing should be shown.</summary>
    public static string? BannerMessage(HardwareMatch match) => match switch
    {
        HardwareMatch.NotFramework => NotFrameworkMessage,
        HardwareMatch.OtherFramework => OtherFrameworkMessage,
        _ => null,
    };

    /// <summary>The trimmed value, or null when it's blank or a placeholder.</summary>
    private static string? Clean(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) || Placeholders.Contains(trimmed) ? null : trimmed;
    }

    private static bool IsLaptop16(string? value) => value is not null && Laptop16().IsMatch(value);

    [GeneratedRegex(@"\bFramework\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex FrameworkWord();

    // \b after 16 rejects "Laptop 160"; \s* also accepts "Laptop16".
    [GeneratedRegex(@"\bLaptop\s*16\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Laptop16();
}
