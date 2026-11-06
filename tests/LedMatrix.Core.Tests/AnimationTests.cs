using LedMatrix.Core;
using LedMatrix.Core.Animation;

namespace LedMatrix.Core.Tests;

public class AnimationTests
{
    private static byte[] Snapshot(LedAnimation a, int w, int h)
    {
        var b = new PixelBuffer(w, h);
        a.Draw(b);
        return b.Data;
    }

    /// <summary>The "congruence" rule: every looping animation ends exactly where it started.</summary>
    [Fact]
    public void Every_looping_animation_returns_exactly_to_its_first_frame()
    {
        var looping = Animations.All.Select(i => i.Create()).Where(a => a.LoopSteps is not null).ToList();
        Assert.True(looping.Count >= 8);
        foreach (var (w, h) in new[] { (9, 34), (18, 34), (24, 34) })
        {
            foreach (var a in looping)
            {
                a.Reset(w, h);
                var first = Snapshot(a, w, h);
                for (var i = 0; i < a.LoopSteps!.Value; i++)
                {
                    a.Step();
                }

                Assert.True(first.AsSpan().SequenceEqual(Snapshot(a, w, h)), $"{a.Id} does not loop on {w}x{h}");
            }
        }
    }

    [Fact]
    public void Reversed_animations_also_return_exactly_to_their_first_frame()
    {
        foreach (var a in Animations.All.Select(i => i.Create()).Where(a => a.LoopSteps is not null))
        {
            a.Reset(9, 34);
            a.Reversed = true;
            var first = Snapshot(a, 9, 34);
            for (var i = 0; i < a.LoopSteps!.Value; i++)
            {
                a.Step();
            }

            Assert.True(first.AsSpan().SequenceEqual(Snapshot(a, 9, 34)), $"{a.Id} does not loop when reversed");
        }
    }

    [Fact]
    public void Reversing_time_plays_the_same_frames_backwards()
    {
        foreach (var info in Animations.All.Where(i => i.ReverseKind == ReverseKind.Time))
        {
            var forward = info.Create();
            forward.Reset(9, 34);
            var loop = forward.LoopSteps!.Value;
            for (var i = 0; i < loop - 1; i++)
            {
                forward.Step();
            }

            var backward = info.Create();
            backward.Reset(9, 34);
            backward.Reversed = true;
            backward.Step(); // one step back = the last frame of the forward loop
            Assert.True(Snapshot(forward, 9, 34).AsSpan().SequenceEqual(Snapshot(backward, 9, 34)), info.Id);
        }
    }

    [Fact]
    public void Reversing_rain_turns_it_upside_down()
    {
        var down = Animations.Find("rain")!.Create();
        var up = Animations.Find("rain")!.Create();
        down.Reset(9, 34, seed: 3);
        up.Reset(9, 34, seed: 3);
        up.Reversed = true;
        for (var i = 0; i < 40; i++)
        {
            down.Step();
            up.Step();
        }

        var a = new PixelBuffer(9, 34);
        var b = new PixelBuffer(9, 34);
        down.Draw(a);
        up.Draw(b);
        Assert.True(a.Data.Any(v => v > 0));
        for (var y = 0; y < 34; y++)
        {
            for (var x = 0; x < 9; x++)
            {
                Assert.Equal(a[x, y], b[x, 33 - y]);
            }
        }
    }

    [Fact]
    public void Diamonds_starts_as_the_diamond_pattern_and_moves_one_row_per_step()
    {
        var pattern = Patterns.Find("Diamond Pattern")!.Create();
        var a = Animations.Find("diamonds")!.Create();
        a.Reset(9, 34);
        Assert.True(pattern.Pixels.SequenceEqual(Snapshot(a, 9, 34)));

        a.Step();
        var moved = Snapshot(a, 9, 34);
        for (var y = 1; y < 34; y++)
        {
            for (var x = 0; x < 9; x++)
            {
                Assert.Equal(pattern[x, y - 1], moved[x + 9 * y]);
            }
        }
    }

    [Fact]
    public void Ball_and_life_have_no_direction_to_reverse()
    {
        Assert.Equal(ReverseKind.None, Animations.Find("ball")!.ReverseKind);
        Assert.Equal(ReverseKind.None, Animations.Find("life")!.ReverseKind);
        Assert.Equal(ReverseKind.None, Animations.Find("breathing")!.ReverseKind);
    }

    [Fact]
    public void Module_scrollable_animations_match_the_firmware_rotation()
    {
        // The firmware moves every column down one row per tick, wrapping the bottom row to the top.
        foreach (var a in Animations.All.Select(i => i.Create()).Where(a => a.ModuleScrollable))
        {
            a.Reset(Frame.Width, Frame.Height);
            var current = Snapshot(a, Frame.Width, Frame.Height);
            for (var step = 0; step < 34; step++)
            {
                var rotated = new byte[current.Length];
                for (var y = 0; y < Frame.Height; y++)
                {
                    for (var x = 0; x < Frame.Width; x++)
                    {
                        rotated[x + Frame.Width * y] = current[x + Frame.Width * ((y + Frame.Height - 1) % Frame.Height)];
                    }
                }

                a.Step();
                current = Snapshot(a, Frame.Width, Frame.Height);
                Assert.True(rotated.AsSpan().SequenceEqual(current), $"{a.Id} differs from the firmware scroll at step {step}");
            }
        }
    }

    [Fact]
    public void All_animations_run_on_every_canvas_shape()
    {
        foreach (var info in Animations.All)
        {
            foreach (var (w, h) in new[] { (9, 34), (18, 34), (24, 34), (9, 68) })
            {
                var a = info.Create();
                a.Reset(w, h);
                var b = new PixelBuffer(w, h);
                for (var i = 0; i < 120; i++)
                {
                    a.Step();
                    a.Draw(b);
                }
            }
        }
    }

    [Fact]
    public void Random_animations_are_repeatable_with_the_same_seed()
    {
        var a = Animations.Find("rain")!.Create();
        var b = Animations.Find("rain")!.Create();
        a.Reset(9, 34, seed: 5);
        b.Reset(9, 34, seed: 5);
        for (var i = 0; i < 50; i++)
        {
            a.Step();
            b.Step();
        }

        Assert.True(Snapshot(a, 9, 34).AsSpan().SequenceEqual(Snapshot(b, 9, 34)));
    }

    [Fact]
    public void Animation_ids_are_unique_and_findable()
    {
        var ids = Animations.All.Select(a => a.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.NotNull(Animations.Find("RAIN"));
        Assert.Null(Animations.Find("nope"));
    }

    [Fact]
    public void Crt_power_off_ends_dark_and_passes_through_a_line()
    {
        var full = Patterns.Find("Full")!.Create();
        Assert.Equal(full, CrtPower.Transform(full, 0));
        var line = CrtPower.Transform(full, 0.5);
        Assert.Equal(255, line[4, 16]);
        Assert.Equal(0, line[4, 0]);
        Assert.All(CrtPower.Transform(full, 1).Pixels.ToArray(), p => Assert.Equal(0, p));
    }

    [Fact]
    public void Crt_filter_dims_scanlines()
    {
        var crt = new CrtFilter();
        var full = Patterns.Find("Full")!.Create();
        var outFrame = crt.Apply(full, tick: 80, intensity: 100); // rolling bar is off-screen at this tick
        Assert.True(outFrame[0, 1] < outFrame[0, 0]);
    }
}
