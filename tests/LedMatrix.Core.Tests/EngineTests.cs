using LedMatrix.Core;
using LedMatrix.Core.Animation;

namespace LedMatrix.Core.Tests;

public sealed class EngineTests : IDisposable
{
    private readonly FakeTime _time = new();
    private readonly Sink _sink = new();
    private readonly AnimationEngine _engine;

    public EngineTests() => _engine = new AnimationEngine(_sink, _time, null, useTimer: false);

    public void Dispose() => _engine.Dispose();

    private sealed class Sink : IFrameSink
    {
        public List<(MatrixSide Side, Frame Frame, ushort? Period)> Shown { get; } = [];

        public void Show(MatrixSide side, Frame frame, ushort? moduleScrollPeriodMs) => Shown.Add((side, frame, moduleScrollPeriodMs));
    }

    private static EngineConfig Config(
        SideContent? left = null, SideContent? right = null, LinkMode link = LinkMode.Independent,
        bool gap = false, bool transitions = false, Frame? leftDrawing = null) => new()
    {
        Left = left ?? new SideContent(),
        Right = right ?? new SideContent(),
        Link = link,
        KeyboardGap = gap,
        CrtTransitions = transitions,
        LeftDrawing = leftDrawing ?? new Frame(),
        RightDrawing = new Frame(),
    };

    private static SideContent Anim(string id, int speed = SideContent.DefaultSpeed) =>
        new() { Kind = ContentKind.Animation, AnimationId = id, Speed = speed };

    private void Advance(double seconds)
    {
        _time.Advance(TimeSpan.FromSeconds(seconds));
        _engine.Tick();
    }

    [Fact]
    public void Still_drawings_are_sent_once_and_the_clock_stays_stopped()
    {
        _engine.Configure(Config(leftDrawing: Patterns.Find("Full")!.Create()));
        Assert.Equal(2, _sink.Shown.Count);
        Assert.Equal(Patterns.Find("Full")!.Create(), _sink.Shown.Single(s => s.Side == MatrixSide.Left).Frame);
        Assert.False(_engine.IsRunning);

        Advance(1);
        Assert.Equal(2, _sink.Shown.Count);
    }

    [Fact]
    public void Animations_run_at_the_chosen_speed()
    {
        _engine.Configure(Config(left: Anim("plasma", speed: 8)));
        Assert.True(_engine.IsRunning);
        var before = _sink.Shown.Count(s => s.Side == MatrixSide.Left);

        Advance(0.05); // less than one step at 8 steps/s: nothing new
        Assert.Equal(before, _sink.Shown.Count(s => s.Side == MatrixSide.Left));

        Advance(0.1); // now 0.15 s: one step due
        Assert.Equal(before + 1, _sink.Shown.Count(s => s.Side == MatrixSide.Left));
    }

    [Fact]
    public void Nothing_is_sent_while_the_leds_are_off_and_the_clock_stops()
    {
        _engine.Configure(Config(left: Anim("rain")));
        _engine.OutputEnabled = false;
        _sink.Shown.Clear();
        Assert.False(_engine.IsRunning);

        Advance(2);
        Assert.Empty(_sink.Shown);

        _engine.OutputEnabled = true; // back on: everything is resent
        Assert.Contains(_sink.Shown, s => s.Side == MatrixSide.Left);
        Assert.Contains(_sink.Shown, s => s.Side == MatrixSide.Right);
        Assert.True(_engine.IsRunning);
    }

    [Fact]
    public void Seam_free_downward_scrolls_are_handed_to_the_module()
    {
        _engine.Configure(Config(left: Anim("checker", speed: 8)));
        var left = _sink.Shown.Single(s => s.Side == MatrixSide.Left);
        Assert.Equal((ushort)125, left.Period);

        // No more traffic for that side, and no clock needed without a preview.
        Advance(1);
        Assert.Single(_sink.Shown, s => s.Side == MatrixSide.Left);
        Assert.False(_engine.IsRunning);

        // The CRT look needs per-frame rendering, so it can't be offloaded.
        var crt = Anim("checker");
        crt.CrtLook = true;
        _engine.Configure(Config(left: crt));
        Assert.Null(_sink.Shown.Last(s => s.Side == MatrixSide.Left).Period);
        Assert.True(_engine.IsRunning);
    }

    [Fact]
    public void A_reversed_scroll_is_played_by_the_app()
    {
        // The firmware only scrolls down.
        var up = Anim("checker");
        up.Reverse = true;
        Assert.False(AnimationEngine.CanRunOnModule(up, LinkMode.Independent));
        _engine.Configure(Config(left: up));
        Assert.Null(_sink.Shown.Last(s => s.Side == MatrixSide.Left).Period);
        Assert.True(_engine.IsRunning);

        up.Reverse = false;
        _engine.Configure(Config(left: up));
        Assert.NotNull(_sink.Shown.Last(s => s.Side == MatrixSide.Left).Period);
    }

    [Fact]
    public void Text_sliding_down_with_a_34_row_loop_runs_on_the_module()
    {
        var text = new SideContent { Kind = ContentKind.Text };
        text.Text.Value = "MIKU";
        text.Text.Font = FontChoice.Regular;
        text.Text.Motion = TextMotion.SlideDown;
        _engine.Configure(Config(left: text)); // 31 rows of text + automatic gap of 3 = 34
        Assert.NotNull(_sink.Shown.Single(s => s.Side == MatrixSide.Left).Period);

        text.Text.Value = "MIKU39"; // 47 rows: longer than the screen, so the app scrolls it
        _engine.Configure(Config(left: text));
        Assert.Null(_sink.Shown.Last(s => s.Side == MatrixSide.Left).Period);
    }

    [Fact]
    public void Mirror_shows_the_left_content_on_both_modules()
    {
        _engine.Configure(Config(left: Anim("helix"), link: LinkMode.Mirror));
        Advance(0.2);
        var l = _sink.Shown.Last(s => s.Side == MatrixSide.Left).Frame;
        var r = _sink.Shown.Last(s => s.Side == MatrixSide.Right).Frame;
        Assert.Equal(l, r);
    }

    [Fact]
    public void Span_splits_one_wide_canvas_with_an_optional_keyboard_gap()
    {
        // Edge to edge (default): column 8 on the left continues in column 0 on the right.
        var stripes = Anim("stripes");
        _engine.Configure(Config(left: stripes, link: LinkMode.Span));
        var l = _sink.Shown.Last(s => s.Side == MatrixSide.Left).Frame;
        var r = _sink.Shown.Last(s => s.Side == MatrixSide.Right).Frame;
        Assert.NotEqual(l, r);

        var expected = new PixelBuffer(18, 34);
        var a = Animations.Find("stripes")!.Create();
        a.Reset(18, 34);
        a.Draw(expected);
        Assert.Equal(expected.ToFrame(9, 0), r);

        _engine.Configure(Config(left: stripes, link: LinkMode.Span, gap: true));
        var withGap = new PixelBuffer(9 * 2 + AnimationEngine.KeyboardGapColumns, 34);
        a.Reset(withGap.Width, 34);
        a.Draw(withGap);
        Assert.Equal(withGap.ToFrame(9 + AnimationEngine.KeyboardGapColumns, 0), _sink.Shown.Last(s => s.Side == MatrixSide.Right).Frame);
    }

    [Fact]
    public void Crt_transition_plays_when_content_changes_and_then_settles()
    {
        _engine.Configure(Config(leftDrawing: Patterns.Find("Full")!.Create(), transitions: true));
        var blank = new SideContent { Kind = ContentKind.Text };
        blank.Text.Value = " ";
        _engine.Configure(Config(left: blank, transitions: true));
        Assert.True(_engine.IsRunning);

        Advance(0.25); // mid power-off: a bright line in the middle
        var mid = _sink.Shown.Last(s => s.Side == MatrixSide.Left).Frame;
        Assert.Equal(255, mid[4, 16]);

        Advance(1.0); // finished: the new (blank) content, clock stopped
        Assert.Equal(new Frame(), _sink.Shown.Last(s => s.Side == MatrixSide.Left).Frame);
        Assert.False(_engine.IsRunning);
    }

    [Fact]
    public void Previews_keep_moving_for_offloaded_scrolls_without_sending()
    {
        var previews = 0;
        _engine.Preview += (_, _) => previews++;
        _engine.PreviewEnabled = true;
        _engine.Configure(Config(left: Anim("checker")));
        var sent = _sink.Shown.Count;
        Assert.True(_engine.IsRunning);

        Advance(0.2);
        Advance(0.2);
        Assert.Equal(sent, _sink.Shown.Count);
        Assert.True(previews > 2);
    }

    [Fact]
    public void The_clock_wakes_only_when_the_next_picture_is_due()
    {
        _engine.Configure(Config(left: Anim("plasma", speed: 8)));
        Assert.Equal(TimeSpan.FromSeconds(1.0 / 8), _engine.NextTickIn);

        _engine.Configure(Config(left: Anim("plasma", speed: 1)));
        Assert.Equal(TimeSpan.FromSeconds(1), _engine.NextTickIn);

        // Fast content is capped at the frame rate.
        _engine.Configure(Config(left: Anim("plasma", speed: 32)));
        Assert.Equal(TimeSpan.FromSeconds(1.0 / AnimationEngine.FramesPerSecond), _engine.NextTickIn);

        // The CRT look runs at its own, lower rate even over a still picture.
        _engine.Configure(Config(left: new SideContent { CrtLook = true }));
        Assert.Equal(TimeSpan.FromSeconds(1.0 / AnimationEngine.CrtFramesPerSecond), _engine.NextTickIn);
    }

    [Fact]
    public void Slow_animations_keep_their_speed_when_the_clock_sleeps_between_steps()
    {
        _engine.Configure(Config(left: Anim("plasma", speed: 1)));
        var start = _sink.Shown.Count(s => s.Side == MatrixSide.Left);
        for (var i = 0; i < 10; i++)
        {
            _time.Advance(_engine.NextTickIn!.Value);
            _engine.Tick();
        }

        // Ten wake-ups, ten steps, ten new pictures: one per second.
        Assert.Equal(start + 10, _sink.Shown.Count(s => s.Side == MatrixSide.Left));
    }

    [Fact]
    public void A_clock_text_wakes_once_a_minute_and_reads_no_stats()
    {
        var stats = new CountingStats();
        using var engine = new AnimationEngine(_sink, _time, stats, useTimer: false);
        var clock = new SideContent { Kind = ContentKind.Text };
        clock.Text.Value = "{time}";
        engine.Configure(Config(left: clock));
        Assert.True(engine.NextTickIn > TimeSpan.FromSeconds(1));
        Assert.True(engine.NextTickIn <= TimeSpan.FromSeconds(61));
        Assert.Equal(0, stats.Reads);

        // With a stats token the text is refreshed every second, and only that stat is read.
        clock.Text.Value = "{cpu}";
        engine.Configure(Config(left: clock));
        Assert.Equal(TimeSpan.FromSeconds(1), engine.NextTickIn);
        Assert.Equal(1, stats.Reads);
    }

    [Fact]
    public void Reconfiguring_with_the_same_content_or_toggling_previews_sends_nothing()
    {
        _engine.Configure(Config(left: Anim("plasma"), leftDrawing: Patterns.Find("Full")!.Create()));
        var sent = _sink.Shown.Count;

        _engine.Configure(Config(left: Anim("plasma"), leftDrawing: Patterns.Find("Full")!.Create()));
        _engine.PreviewEnabled = true;
        _engine.PreviewEnabled = false;
        Assert.Equal(sent, _sink.Shown.Count);
    }

    [Fact]
    public void Editing_one_module_leaves_a_firmware_scroll_on_the_other_alone()
    {
        _engine.Configure(Config(left: new SideContent(), right: Anim("checker")));
        Assert.Single(_sink.Shown, s => s.Side == MatrixSide.Right);

        var drawing = new Frame();
        drawing[3, 3] = 255;
        _engine.Configure(Config(left: new SideContent(), right: Anim("checker"), leftDrawing: drawing));
        Assert.Equal(drawing, _sink.Shown.Last(s => s.Side == MatrixSide.Left).Frame);
        Assert.Single(_sink.Shown, s => s.Side == MatrixSide.Right);

        // A new speed restarts the module's scroll with the new period.
        _engine.Configure(Config(left: new SideContent(), right: Anim("checker", speed: 4), leftDrawing: drawing));
        Assert.Equal((ushort)250, _sink.Shown.Last(s => s.Side == MatrixSide.Right).Period);
    }

    [Fact]
    public void Changing_the_link_mode_or_switching_the_crt_look_off_updates_the_modules()
    {
        var full = Patterns.Find("Full")!.Create();
        _engine.Configure(Config(leftDrawing: full));
        Assert.Equal(new Frame(), _sink.Shown.Last(s => s.Side == MatrixSide.Right).Frame);

        // Mirror: the right module now shows the left drawing.
        _engine.Configure(Config(leftDrawing: full, link: LinkMode.Mirror));
        Assert.Equal(full, _sink.Shown.Last(s => s.Side == MatrixSide.Right).Frame);

        // CRT look on, then off again: the plain picture comes back.
        _engine.Configure(Config(left: new SideContent { CrtLook = true }, leftDrawing: full));
        Assert.NotEqual(full, _sink.Shown.Last(s => s.Side == MatrixSide.Left).Frame);
        _engine.Configure(Config(left: new SideContent(), leftDrawing: full));
        Assert.Equal(full, _sink.Shown.Last(s => s.Side == MatrixSide.Left).Frame);
        Assert.False(_engine.IsRunning);
    }

    private sealed class CountingStats : ISystemStats
    {
        public int Reads { get; private set; }

        public int? BatteryPercent => Count(80);

        public int? CpuPercent => Count(5);

        public int? RamPercent => Count(40);

        private int Count(int value)
        {
            Reads++;
            return value;
        }
    }

    [Fact]
    public void Counters_track_frames_scrolls_and_previews()
    {
        _engine.PreviewEnabled = true;
        _engine.Configure(Config(left: Anim("plasma"), right: Anim("checker")));
        Advance(0.2);
        var c = _engine.Counters;
        Assert.Equal(_sink.Shown.Count(s => s.Side == MatrixSide.Left), c.LeftFramesSent);
        Assert.Equal(_sink.Shown.Count(s => s.Side == MatrixSide.Right), c.RightFramesSent);
        Assert.Equal(1, c.ScrollStarts);
        Assert.True(c.PreviewFrames > 0);
        Assert.Equal(0, c.TimerTicks); // only real timer callbacks count as wake-ups
    }

    [Fact]
    public void Scroll_period_follows_speed()
    {
        Assert.Equal((ushort)125, AnimationEngine.ScrollPeriod(8));
        Assert.Equal((ushort)31, AnimationEngine.ScrollPeriod(32));
        Assert.Equal((ushort)1000, AnimationEngine.ScrollPeriod(1));
    }
}
