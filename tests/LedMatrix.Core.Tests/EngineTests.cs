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
        _engine.Configure(Config(left: Anim("sine", speed: 8)));
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
    public void Scroll_period_follows_speed()
    {
        Assert.Equal((ushort)125, AnimationEngine.ScrollPeriod(8));
        Assert.Equal((ushort)31, AnimationEngine.ScrollPeriod(32));
        Assert.Equal((ushort)1000, AnimationEngine.ScrollPeriod(1));
    }
}
