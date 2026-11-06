using LedMatrix.Core.Animation;

namespace LedMatrix.Core.Tests;

public class TextTests
{
    [Fact]
    public void Capacities_match_the_proposal()
    {
        Assert.Equal(4, TextRenderer.Capacity(PixelFont.Regular, TextLayout.Stacked, 34));
        Assert.Equal(5, TextRenderer.Capacity(PixelFont.Tiny, TextLayout.Stacked, 34));
        Assert.Equal(5, TextRenderer.Capacity(PixelFont.Regular, TextLayout.Sideways, 34));
        Assert.Equal(8, TextRenderer.Capacity(PixelFont.Tiny, TextLayout.Sideways, 34));
        Assert.Equal(8, TextRenderer.Capacity(PixelFont.Regular, TextLayout.Stacked, 68)); // Span
    }

    [Fact]
    public void Auto_fit_picks_the_largest_font_that_fits()
    {
        var miku = TextRenderer.Plan("MIKU", TextLayout.Stacked, FontChoice.Auto, 34);
        Assert.Same(PixelFont.Regular, miku.Font);
        Assert.True(miku.Fits);
        Assert.Equal(5, miku.Bitmap.Width);
        Assert.Equal(4 * 8 - 1, miku.Bitmap.Height);

        var hello = TextRenderer.Plan("HELLO", TextLayout.Stacked, FontChoice.Auto, 34);
        Assert.Same(PixelFont.Tiny, hello.Font);
        Assert.True(hello.Fits);

        var tooLong = TextRenderer.Plan("HELLO DEO", TextLayout.Stacked, FontChoice.Auto, 34);
        Assert.False(tooLong.Fits);
        Assert.Equal(5, tooLong.Capacity);
    }

    [Fact]
    public void Static_text_is_centred()
    {
        var plan = TextRenderer.Plan("MIKU", TextLayout.Stacked, FontChoice.Regular, 34);
        var b = new PixelBuffer(9, 34);
        TextRenderer.Draw(plan, TextMotion.Static, 6, 0, b);
        // 31 rows of text in 34: one blank row above, two below; 5 columns in 9: two blank each side.
        Assert.All(Enumerable.Range(0, 9), x => Assert.Equal(0, b[x, 0]));
        Assert.Equal(255, b[2, 1]); // top-left of 'M'
        Assert.All(Enumerable.Range(0, 34), y => Assert.Equal(0, b[0, y]));
    }

    [Fact]
    public void Sliding_text_loops_exactly()
    {
        foreach (var (layout, motion) in new[]
                 {
                     (TextLayout.Stacked, TextMotion.SlideUp), (TextLayout.Stacked, TextMotion.SlideDown),
                     (TextLayout.Sideways, TextMotion.SlideUp), (TextLayout.Ticker, TextMotion.SlideLeft),
                     (TextLayout.Ticker, TextMotion.SlideRight),
                 })
        {
            var plan = TextRenderer.Plan("HELLO DEO", layout, FontChoice.Auto, 34);
            var a = new PixelBuffer(9, 34);
            var b = new PixelBuffer(9, 34);
            TextRenderer.Draw(plan, motion, 6, 3, a);
            TextRenderer.Draw(plan, motion, 6, 3 + plan.LoopLength(6), b);
            Assert.True(a.Data.AsSpan().SequenceEqual(b.Data), $"{layout}/{motion}");
        }
    }

    [Fact]
    public void Gap_is_automatic()
    {
        // Text that fits wraps around the screen: one loop is exactly one screen.
        var miku = TextRenderer.Plan("MIKU", TextLayout.Stacked, FontChoice.Regular, 34);
        Assert.Equal(3, miku.Gap);
        Assert.Equal(34, miku.LoopLength());

        var hi = TextRenderer.Plan("HI", TextLayout.Stacked, FontChoice.Regular, 68); // Span
        Assert.Equal(68, hi.LoopLength());

        // Longer text, or too little room left for a visible break, gets the default gap.
        Assert.Equal(TextRenderer.DefaultGap, TextRenderer.Plan("HELLO DEO", TextLayout.Stacked, FontChoice.Auto, 34).Gap);
        Assert.Equal(TextRenderer.DefaultGap, TextRenderer.AutoGap(33, 34));
        Assert.Equal(TextRenderer.DefaultGap, TextRenderer.Plan("HI", TextLayout.Ticker, FontChoice.Auto, 34).Gap);
    }

    [Fact]
    public void Sliding_short_text_loops_exactly_with_the_automatic_gap()
    {
        foreach (var motion in new[] { TextMotion.SlideUp, TextMotion.SlideDown })
        {
            var plan = TextRenderer.Plan("MIKU", TextLayout.Stacked, FontChoice.Auto, 34);
            var a = new PixelBuffer(9, 34);
            var b = new PixelBuffer(9, 34);
            TextRenderer.Draw(plan, motion, 5, a);
            TextRenderer.Draw(plan, motion, 5 + plan.LoopLength(), b);
            Assert.True(a.Data.AsSpan().SequenceEqual(b.Data), motion.ToString());
        }
    }

    [Fact]
    public void Sideways_text_is_rotated_clockwise()
    {
        // 'L' in Tiny: a vertical bar on the left and a foot at the bottom. Rotated clockwise, the
        // bar becomes the top edge... read top-to-bottom, the foot ends up on the left.
        var b = TextRenderer.Render("L", PixelFont.Tiny, TextLayout.Sideways);
        Assert.Equal(5, b.Width);
        Assert.Equal(3, b.Height);
        Assert.Equal(255, b[0, 2]); // foot (glyph row 4) -> column 0
        Assert.Equal(255, b[4, 0]); // top of the bar (glyph row 0) -> column 4
    }

    [Fact]
    public void Unknown_characters_render_as_question_marks()
    {
        var a = TextRenderer.Render("~", PixelFont.Regular, TextLayout.Stacked);
        var b = TextRenderer.Render("?", PixelFont.Regular, TextLayout.Stacked);
        Assert.True(a.Data.AsSpan().SequenceEqual(b.Data));
    }

    private sealed class Stats : ISystemStats
    {
        public int? BatteryPercent => 87;

        public int? CpuPercent => null;

        public int? RamPercent => 39;
    }

    [Fact]
    public void Tokens_expand()
    {
        var now = new DateTimeOffset(2026, 11, 5, 7, 12, 0, TimeSpan.Zero);
        Assert.Equal("07:12", TextTokens.Expand("{time}", now, null));
        Assert.Equal("NOV 5", TextTokens.Expand("{DATE}", now, null));
        Assert.Equal("87% -- 39%", TextTokens.Expand("{battery} {cpu} {ram}", now, new Stats()));
        Assert.Equal("--", TextTokens.Expand("{battery}", now, null));
        Assert.Equal("plain", TextTokens.Expand("plain", now, null));
    }
}
