using LedMatrix.Core;
using Microsoft.Win32;

namespace LedMatrix.App.Services;

/// <summary>
/// Which PC this is, from the SMBIOS values Windows copies into the registry at boot
/// (no WMI needed). Read once on first use, since it can't change while the app runs.
/// </summary>
internal static class HardwareInfo
{
    private const string BiosKey = @"HARDWARE\DESCRIPTION\System\BIOS";

    private static readonly Lazy<HardwareMatch> Detected = new(Detect);

    /// <summary>How this PC compares to a Framework Laptop 16 (Unknown when it can't be read).</summary>
    public static HardwareMatch Match => Detected.Value;

    /// <summary>The main window banner for this PC, or null for none.</summary>
    public static string? BannerMessage => HardwareCheck.BannerMessage(Match);

    private static HardwareMatch Detect()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(BiosKey);
            if (key is null)
            {
                Log.Warn($"Could not find HKLM\\{BiosKey}");
                return HardwareMatch.Unknown;
            }

            var manufacturer = key.GetValue("SystemManufacturer") as string;
            var product = key.GetValue("SystemProductName") as string;
            var family = key.GetValue("SystemFamily") as string;
            var match = HardwareCheck.Classify(manufacturer, product, family);
            Log.Info($"Hardware: {manufacturer} / {product} / {family} ({match})");
            return match;
        }
        catch (Exception ex)
        {
            Log.Warn("Could not read the hardware model", ex);
            return HardwareMatch.Unknown;
        }
    }
}
