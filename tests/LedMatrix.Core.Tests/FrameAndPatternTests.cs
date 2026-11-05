using LedMatrix.Core;

namespace LedMatrix.Core.Tests;

public class FrameAndPatternTests
{
    [Fact]
    public void Frame_indexes_by_column_and_row()
    {
        var f = new Frame();
        f[8, 33] = 200;
        Assert.Equal(200, f[8, 33]);
        Assert.Equal(200, f.Pixels[8 + 9 * 33]);
        Assert.Throws<ArgumentOutOfRangeException>(() => f[9, 0]);
    }

    [Fact]
    public void Frame_round_trips_through_base64()
    {
        var f = Patterns.Find("Gradient")!.Create();
        var copy = Frame.TryFromBase64(f.ToBase64());
        Assert.Equal(f, copy);
        Assert.Null(Frame.TryFromBase64("not base64!"));
        Assert.Null(Frame.TryFromBase64(Convert.ToBase64String(new byte[5])));
    }

    [Fact]
    public void Invert_and_bw_detection()
    {
        var f = new Frame();
        Assert.True(f.IsBlackAndWhite());
        f.Invert();
        Assert.Equal(255, f[0, 0]);
        f[1, 1] = 7;
        Assert.False(f.IsBlackAndWhite());
    }

    [Fact]
    public void Pattern_names_are_unique_and_all_render()
    {
        var names = Patterns.All.Select(p => p.Name).ToList();
        Assert.Equal(names.Count, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        foreach (var p in Patterns.All)
        {
            Assert.NotNull(p.Create());
        }
    }

    [Fact]
    public void Blank_full_and_checkerboard()
    {
        Assert.All(Patterns.Find("Blank")!.Create().Pixels.ToArray(), p => Assert.Equal(0, p));
        Assert.All(Patterns.Find("Full")!.Create().Pixels.ToArray(), p => Assert.Equal(255, p));

        var c = Patterns.Find("Checkerboard")!.Create();
        Assert.NotEqual(c[0, 0], c[1, 0]);
        Assert.NotEqual(c[0, 0], c[0, 1]);
        Assert.Equal(c[0, 0], c[1, 1]);
    }

    [Fact]
    public void Scatter_lights_exactly_half()
    {
        var f = Patterns.Find("Scatter 50%")!.Create();
        Assert.Equal(Frame.PixelCount / 2, f.Pixels.ToArray().Count(p => p == 255));
    }

    [Fact]
    public void Percentage_fills_from_the_bottom()
    {
        var f = Patterns.Percentage(50);
        Assert.Equal(0, f[0, 0]);
        Assert.Equal(255, f[0, 33]);
        Assert.Equal(17 * 9, f.Pixels.ToArray().Count(p => p == 255));
    }
}
