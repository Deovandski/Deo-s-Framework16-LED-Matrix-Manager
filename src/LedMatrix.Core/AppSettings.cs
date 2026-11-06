using System.Text.Json;
using System.Text.Json.Serialization;
using LedMatrix.Core.Animation;

namespace LedMatrix.Core;

public enum MatrixSide
{
    Left,
    Right,
}

/// <summary>Persisted user settings (%AppData%\DeoLedMatrixManager\settings.json).</summary>
public sealed class AppSettings
{
    /// <summary>39% by default (a Hatsune Miku easter egg: 39 = "mi-ku").</summary>
    public const byte DefaultBrightness = 99; // 99 / 255 = 38.8% -> shown as 39%

    public int SchemaVersion { get; set; } = 2;

    public byte Brightness { get; set; } = DefaultBrightness;

    public byte BrushBrightness { get; set; } = 255;

    public LedOverride Override { get; set; } = LedOverride.Auto;

    public AutoOffOptions AutoOff { get; set; } = new();

    /// <summary>USB serial number of the module assigned to the Left / Right role.</summary>
    public string? LeftSerial { get; set; }

    public string? RightSerial { get; set; }

    /// <summary>User-given names, keyed by USB serial number.</summary>
    public Dictionary<string, string> DeviceNames { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Last content per side, as base64 of the 306 pixel bytes.</summary>
    public string? LeftFrame { get; set; }

    public string? RightFrame { get; set; }

    public bool FirstRunCompleted { get; set; }

    /// <summary>What each module shows (drawing, animation or text).</summary>
    public SideContent LeftContent { get; set; } = new();

    public SideContent RightContent { get; set; } = new();

    public LinkMode Link { get; set; } = LinkMode.Independent;

    /// <summary>Span mode: hide a few virtual columns between the modules (off = edge to edge).</summary>
    public bool KeyboardGap { get; set; }

    /// <summary>Play the CRT power off/on effect when the content changes.</summary>
    public bool CrtTransitions { get; set; }

    public SideContent GetContent(MatrixSide side) => side == MatrixSide.Left ? LeftContent : RightContent;

    public string? GetSerial(MatrixSide side) => side == MatrixSide.Left ? LeftSerial : RightSerial;

    public void SetSerial(MatrixSide side, string? serial)
    {
        if (side == MatrixSide.Left)
        {
            LeftSerial = serial;
        }
        else
        {
            RightSerial = serial;
        }
    }

    public Frame GetFrame(MatrixSide side) =>
        Frame.TryFromBase64(side == MatrixSide.Left ? LeftFrame : RightFrame) ?? new Frame();

    public void SetFrame(MatrixSide side, Frame frame)
    {
        if (side == MatrixSide.Left)
        {
            LeftFrame = frame.ToBase64();
        }
        else
        {
            RightFrame = frame.ToBase64();
        }
    }
}

[JsonSourceGenerationOptions(WriteIndented = true, UseStringEnumConverter = true)]
[JsonSerializable(typeof(AppSettings))]
internal sealed partial class SettingsJsonContext : JsonSerializerContext;

public static class SettingsStore
{

    public static AppSettings Load(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                var settings = JsonSerializer.Deserialize(File.ReadAllText(path), SettingsJsonContext.Default.AppSettings);
                if (settings is not null)
                {
                    settings.AutoOff ??= new AutoOffOptions();
                    settings.LeftContent ??= new SideContent();
                    settings.RightContent ??= new SideContent();
                    settings.LeftContent.Text ??= new TextSettings();
                    settings.RightContent.Text ??= new TextSettings();
                    settings.DeviceNames = new Dictionary<string, string>(
                        settings.DeviceNames ?? [], StringComparer.OrdinalIgnoreCase);
                    return settings;
                }
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Settings at {path} could not be read; using defaults", ex);
            TryBackupCorrupt(path);
        }

        return new AppSettings();
    }

    /// <summary>Writes atomically (temp file + replace) so a crash never leaves a half-written file.</summary>
    public static void Save(string path, AppSettings settings) => Write(path, Serialize(settings));

    public static string Serialize(AppSettings settings) =>
        JsonSerializer.Serialize(settings, SettingsJsonContext.Default.AppSettings);

    /// <summary>Writes <paramref name="json"/> atomically (temp file + replace).</summary>
    public static void Write(string path, string json)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var tmp = path + ".tmp";
        File.WriteAllText(tmp, json);
        File.Move(tmp, path, overwrite: true);
    }

    private static void TryBackupCorrupt(string path)
    {
        try
        {
            File.Copy(path, path + ".corrupt", overwrite: true);
        }
        catch (Exception)
        {
            // Best effort only.
        }
    }
}

/// <summary>Maps detected modules (by USB serial) onto the Left/Right roles, remembering choices.</summary>
public static class RoleAssigner
{
    /// <summary>
    /// Returns the serial for each side. Known serials keep their side; new modules fill empty
    /// sides in the given order (callers pass serials sorted by COM port number). Updates
    /// <paramref name="settings"/> when a new module takes over a side.
    /// </summary>
    public static (string? Left, string? Right) Assign(IReadOnlyList<string> presentSerials, AppSettings settings)
    {
        var present = new HashSet<string>(presentSerials, StringComparer.OrdinalIgnoreCase);
        string? left = settings.LeftSerial is { } l && present.Contains(l) ? l : null;
        string? right = settings.RightSerial is { } r && present.Contains(r) ? r : null;

        foreach (var serial in presentSerials)
        {
            if (string.Equals(serial, left, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(serial, right, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (left is null)
            {
                left = serial;
                settings.LeftSerial = serial;
            }
            else if (right is null)
            {
                right = serial;
                settings.RightSerial = serial;
            }
        }

        return (left, right);
    }

    /// <summary>Swaps which physical module is Left/Right. The drawings stay on their side of the screen.</summary>
    public static void Swap(AppSettings settings) =>
        (settings.LeftSerial, settings.RightSerial) = (settings.RightSerial, settings.LeftSerial);
}
