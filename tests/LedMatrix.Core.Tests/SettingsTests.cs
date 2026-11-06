using LedMatrix.Core;

namespace LedMatrix.Core.Tests;

public class SettingsTests
{
    [Fact]
    public void Settings_round_trip_through_json()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ledmatrix-test-{Guid.NewGuid():N}", "settings.json");
        try
        {
            var s = new AppSettings
            {
                Brightness = 77,
                Override = LedOverride.AlwaysOff,
                LeftSerial = "AAA",
            };
            s.AutoOff.WhenLocked = true;
            s.DeviceNames["AAA"] = "Left one";
            s.SetFrame(MatrixSide.Right, Patterns.Find("Gradient")!.Create());

            SettingsStore.Save(path, s);
            var loaded = SettingsStore.Load(path);

            Assert.Equal(77, loaded.Brightness);
            Assert.Equal(LedOverride.AlwaysOff, loaded.Override);
            Assert.True(loaded.AutoOff.WhenLocked);
            Assert.Equal("Left one", loaded.DeviceNames["aaa"]); // case-insensitive serial lookup
            Assert.Equal(Patterns.Find("Gradient")!.Create(), loaded.GetFrame(MatrixSide.Right));
            Assert.Contains("\"AlwaysOff\"", File.ReadAllText(path));
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    [Fact]
    public void Corrupt_or_missing_settings_fall_back_to_defaults()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"ledmatrix-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "settings.json");
            Assert.Equal(AppSettings.DefaultBrightness, SettingsStore.Load(path).Brightness);

            File.WriteAllText(path, "{ not json");
            Assert.Equal(AppSettings.DefaultBrightness, SettingsStore.Load(path).Brightness);
            Assert.True(File.Exists(path + ".corrupt"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Default_brightness_is_39_percent()
    {
        // Hatsune Miku easter egg: 39 = "mi-ku".
        Assert.Equal(39, (int)Math.Round(new AppSettings().Brightness / 255.0 * 100));
    }

    [Fact]
    public void Content_settings_round_trip_and_old_files_get_defaults()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"ledmatrix-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "settings.json");
            var s = new AppSettings { Link = LedMatrix.Core.Animation.LinkMode.Span, KeyboardGap = true };
            s.LeftContent.Kind = LedMatrix.Core.Animation.ContentKind.Text;
            s.LeftContent.Text.Value = "MIKU";
            SettingsStore.Save(path, s);
            var loaded = SettingsStore.Load(path);
            Assert.Equal(LedMatrix.Core.Animation.LinkMode.Span, loaded.Link);
            Assert.True(loaded.KeyboardGap);
            Assert.Equal("MIKU", loaded.LeftContent.Text.Value);

            // A v1 settings file (no content fields) loads with the new defaults.
            File.WriteAllText(path, "{\"SchemaVersion\":1,\"Brightness\":100}");
            var old = SettingsStore.Load(path);
            Assert.Equal(100, old.Brightness);
            Assert.Equal(LedMatrix.Core.Animation.ContentKind.Drawing, old.LeftContent.Kind);
            Assert.Equal(LedMatrix.Core.Animation.LinkMode.Independent, old.Link);
            Assert.False(old.KeyboardGap);
            Assert.Equal(LedMatrix.Core.Animation.SideContent.DefaultSpeed, old.RightContent.Speed);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Roles_keep_known_modules_and_fill_gaps_with_new_ones()
    {
        var s = new AppSettings { LeftSerial = "L", RightSerial = "R" };
        Assert.Equal(("L", "R"), RoleAssigner.Assign(["R", "L"], s));

        // Right module replaced with a new one
        Assert.Equal(("L", "NEW"), RoleAssigner.Assign(["L", "NEW"], s));
        Assert.Equal("NEW", s.RightSerial);

        // Only one module installed
        var fresh = new AppSettings();
        Assert.Equal(("ONLY", null), RoleAssigner.Assign(["ONLY"], fresh));

        RoleAssigner.Swap(s);
        Assert.Equal(("NEW", "L"), (s.LeftSerial, s.RightSerial));
    }
}
