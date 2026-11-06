namespace LedMatrix.Core.Animation;

/// <summary>Receives the frames the engine wants on each module.</summary>
public interface IFrameSink
{
    /// <param name="moduleScrollPeriodMs">
    /// Non-null: the module should scroll this frame down by itself, one row every N ms (firmware
    /// Animate), and no further frames will follow. Null: a normal still frame.
    /// </param>
    void Show(MatrixSide side, Frame frame, ushort? moduleScrollPeriodMs);
}

/// <summary>Running totals since the engine started (for the diagnostics log).</summary>
public readonly record struct EngineCounters(
    long TimerTicks, long LeftFramesSent, long RightFramesSent, long ScrollStarts, long PreviewFrames);

/// <summary>Everything the engine needs to know about what to show.</summary>
public sealed class EngineConfig
{
    public LinkMode Link { get; init; }

    public bool KeyboardGap { get; init; }

    public bool CrtTransitions { get; init; }

    public required SideContent Left { get; init; }

    public required SideContent Right { get; init; }

    public required Frame LeftDrawing { get; init; }

    public required Frame RightDrawing { get; init; }
}

/// <summary>
/// Turns content (drawings, animations, text) into module frames. One shared clock drives both
/// modules so they stay in sync. The clock is a one-shot timer re-armed for the moment the next
/// picture is due (a calm animation wakes 8 times a second, a {time} clock once a minute, a still
/// picture never), capped at <see cref="FramesPerSecond"/>. A frame is only sent when the picture
/// changed. Seam-free downward scrolling is handed to the module's firmware, which then needs no
/// traffic at all.
/// </summary>
public sealed class AnimationEngine : IDisposable
{
    public const int FramesPerSecond = 20;

    /// <summary>The CRT look's own frame rate: its rolling bar and flicker don't need more.</summary>
    public const int CrtFramesPerSecond = 10;

    private static readonly TimeSpan MinTickInterval = TimeSpan.FromSeconds(1.0 / FramesPerSecond);
    private static readonly TimeSpan CrtTickInterval = TimeSpan.FromSeconds(1.0 / CrtFramesPerSecond);

    /// <summary>Hidden virtual columns between the modules in Span mode with the keyboard gap on.</summary>
    public const int KeyboardGapColumns = 6;

    private static readonly MatrixSide[] Sides = [MatrixSide.Left, MatrixSide.Right];

    private readonly IFrameSink _sink;
    private readonly TimeProvider _time;
    private readonly ISystemStats? _stats;
    private readonly object _gate = new();
    private readonly Timer _timer;
    private readonly bool _useTimer;
    private readonly Dictionary<MatrixSide, Output> _outputs = [];
    private readonly Dictionary<MatrixSide, SideState> _state = new()
    {
        [MatrixSide.Left] = new SideState(),
        [MatrixSide.Right] = new SideState(),
    };

    private readonly DateTimeOffset _epoch;
    private Source[] _sources = [];
    private EngineConfig? _config;
    private DateTimeOffset _lastTick;
    private TimeSpan? _armedFor;
    private bool _forceSend = true;
    private bool _forcePreview = true;
    private bool _outputEnabled = true;
    private bool _previewEnabled;
    private bool _disposed;
    private long _timerTicks;
    private long _leftFrames;
    private long _rightFrames;
    private long _scrollStarts;
    private long _previewFrames;

    public AnimationEngine(IFrameSink sink, TimeProvider? time = null, ISystemStats? stats = null)
        : this(sink, time, stats, useTimer: true)
    {
    }

    /// <param name="useTimer">False in tests: <see cref="Tick"/> is then called by hand.</param>
    internal AnimationEngine(IFrameSink sink, TimeProvider? time, ISystemStats? stats, bool useTimer)
    {
        _useTimer = useTimer;
        _sink = sink;
        _time = time ?? TimeProvider.System;
        _stats = stats;
        _lastTick = _epoch = _time.GetUtcNow();
        _timer = new Timer(_ => TimerTick(), null, Timeout.Infinite, Timeout.Infinite);
    }

    /// <summary>Every frame shown on a module (also offloaded ones, simulated), for on-screen previews.</summary>
    public event Action<MatrixSide, Frame>? Preview;

    /// <summary>True while the main window is visible; keeps previews moving even for offloaded scrolls.</summary>
    public bool PreviewEnabled
    {
        get { lock (_gate) { return _previewEnabled; } }
        set
        {
            lock (_gate)
            {
                if (_previewEnabled == value)
                {
                    return;
                }

                // Only the previews need a fresh copy; the modules already show the right thing.
                _previewEnabled = value;
                _forcePreview = true;
                TickLocked();
            }
        }
    }

    /// <summary>False while the LEDs are off: nothing is sent and the clock stops unless a preview needs it.</summary>
    public bool OutputEnabled
    {
        get { lock (_gate) { return _outputEnabled; } }
        set
        {
            lock (_gate)
            {
                if (_outputEnabled == value)
                {
                    return;
                }

                _outputEnabled = value;
                foreach (var s in _state.Values)
                {
                    s.Offloaded = false;
                    s.LastSent = null;
                    s.Transition = null;
                }

                _forceSend = true;
                TickLocked();
            }
        }
    }

    /// <summary>True while the clock is armed for another tick (for diagnostics and tests).</summary>
    public bool IsRunning
    {
        get { lock (_gate) { return _armedFor is not null; } }
    }

    /// <summary>Running totals since start: timer wake-ups, frames sent per module, firmware scrolls started, previews.</summary>
    public EngineCounters Counters
    {
        get { lock (_gate) { return new(_timerTicks, _leftFrames, _rightFrames, _scrollStarts, _previewFrames); } }
    }

    /// <summary>How long until the next tick, or null when the clock is stopped (tests, benchmarks).</summary>
    internal TimeSpan? NextTickIn
    {
        get { lock (_gate) { return _armedFor; } }
    }

    /// <summary>Applies new content. Animations keep their state when only speed or effects change.</summary>
    public void Configure(EngineConfig config)
    {
        lock (_gate)
        {
            var previous = new Dictionary<MatrixSide, string?>();
            foreach (var side in Sides)
            {
                previous[side] = _outputs.TryGetValue(side, out var o) ? o.Source.Key : null;
            }

            BuildOutputs(config, [.. _sources]);
            _config = config;

            foreach (var side in Sides)
            {
                var st = _state[side];
                st.Dirty = true; // recompute the frame; it's only sent if it actually differs
                var owner = OwnerContent(config, side);
                var crtWasOn = st.CrtOn;
                st.CrtOn = owner.CrtLook;
                st.CrtIntensity = owner.CrtIntensity;
                if (st.CrtOn && !crtWasOn)
                {
                    st.Crt.Reset();
                }

                if (config.CrtTransitions && _outputEnabled && previous[side] is { } prevKey &&
                    prevKey != _outputs[side].Source.Key && st.LastFrame is { } from)
                {
                    st.Transition = new Transition(_time.GetUtcNow(), from);
                }
            }

            TickLocked();
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _timer.Change(Timeout.Infinite, Timeout.Infinite);
        }

        _timer.Dispose();
    }

    /// <summary>One clock tick. Called by the timer; exposed to tests.</summary>
    internal void Tick()
    {
        lock (_gate)
        {
            TickLocked();
        }
    }

    private static SideContent OwnerContent(EngineConfig c, MatrixSide side) =>
        c.Link == LinkMode.Independent && side == MatrixSide.Right ? c.Right : c.Left;

    private void TimerTick()
    {
        // The timer is one-shot and every tick re-arms it, so ticks can't pile up; wait for the
        // lock rather than skip, or a tick that met a brief lock (a property read) would stop the clock.
        lock (_gate)
        {
            try
            {
                if (!_disposed)
                {
                    _timerTicks++;
                    TickLocked();
                }
            }
            catch (Exception ex)
            {
                Log.Error("Animation tick failed", ex);
                Arm(MinTickInterval); // keep going rather than freeze
            }
        }
    }

    private void TickLocked()
    {
        if (_config is null || _disposed)
        {
            return;
        }

        var now = _time.GetUtcNow();

        // Time only flows while the clock runs: content that was paused doesn't jump ahead, and a
        // tick that comes very late (system busy or suspended) is limited to the planned wait + 0.5 s.
        var dt = _armedFor is { } planned ? Math.Clamp((now - _lastTick).TotalSeconds, 0, planned.TotalSeconds + 0.5) : 0;
        _lastTick = now;

        foreach (var src in _sources)
        {
            var c = src.Advance(dt, now) | src.NeedsRender;
            if (c)
            {
                src.Render();
                src.NeedsRender = false;
            }

            src.ChangedThisTick = c;
        }

        TimeSpan? due = null;
        foreach (var side in Sides)
        {
            var output = _outputs[side];
            var st = _state[side];
            var src = output.Source;
            var offload = src.ModuleScrollable && _config.Link != LinkMode.Span && !st.CrtOn && st.Transition is null;

            Frame frame;
            if (src.ChangedThisTick || st.LastFrame is null || st.Dirty || st.CrtOn || st.Transition is not null)
            {
                st.Dirty = false;
                frame = src.Canvas.ToFrame(output.Left, output.Top);
                if (st.CrtOn)
                {
                    frame = st.Crt.Apply(frame, (now - _epoch).TotalSeconds, st.CrtIntensity);
                }

                if (st.Transition is { } tr)
                {
                    var elapsed = now - tr.Start;
                    var phase = CrtPower.PhaseDuration.TotalMilliseconds;
                    if (elapsed.TotalMilliseconds < phase)
                    {
                        frame = CrtPower.Transform(tr.From, elapsed.TotalMilliseconds / phase);
                    }
                    else if (elapsed.TotalMilliseconds < 2 * phase)
                    {
                        frame = CrtPower.Transform(frame, 1 - (elapsed.TotalMilliseconds - phase) / phase);
                    }
                    else
                    {
                        st.Transition = null;
                    }
                }
            }
            else
            {
                frame = st.LastFrame;
            }

            var frameChanged = st.LastFrame is null || !frame.Equals(st.LastFrame);
            st.LastFrame = frame;

            if (_previewEnabled && (frameChanged || _forcePreview))
            {
                _previewFrames++;
                Preview?.Invoke(side, frame);
            }

            if (_outputEnabled)
            {
                if (offload)
                {
                    // The module scrolls by itself: only (re)start it for new content or a new speed.
                    var period = ScrollPeriod(src.Speed);
                    if (!st.Offloaded || _forceSend || !ReferenceEquals(st.OffloadSource, src) || st.OffloadPeriod != period)
                    {
                        _sink.Show(side, frame, period);
                        _scrollStarts++;
                        CountSent(side);
                        st.Offloaded = true;
                        st.OffloadSource = src;
                        st.OffloadPeriod = period;
                        st.LastSent = frame;
                    }
                }
                else if (st.Offloaded || _forceSend || st.LastSent is null || !frame.Equals(st.LastSent))
                {
                    _sink.Show(side, frame, null);
                    CountSent(side);
                    st.Offloaded = false;
                    st.OffloadSource = null;
                    st.LastSent = frame;
                }
            }

            // When is this side's picture next going to change?
            if (src.Dynamic && (!offload || !_outputEnabled || _previewEnabled))
            {
                due = Earliest(due, src.NextDue(now));
            }

            if (st.CrtOn)
            {
                due = Earliest(due, CrtTickInterval);
            }

            if (st.Transition is not null)
            {
                due = Earliest(due, MinTickInterval);
            }
        }

        _forceSend = false;
        _forcePreview = false;
        Arm(_outputEnabled || _previewEnabled ? due : null);
    }

    private void CountSent(MatrixSide side)
    {
        if (side == MatrixSide.Left)
        {
            _leftFrames++;
        }
        else
        {
            _rightFrames++;
        }
    }

    private static TimeSpan? Earliest(TimeSpan? a, TimeSpan? b) =>
        a is null ? b : b is null ? a : a < b ? a : b;

    /// <summary>Arms the one-shot timer for the next due picture (never sooner than the frame-rate cap).</summary>
    private void Arm(TimeSpan? due)
    {
        _armedFor = due is { } d ? (d < MinTickInterval ? MinTickInterval : d) : null;
        if (_useTimer)
        {
            _timer.Change(_armedFor ?? Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }
    }

    /// <summary>
    /// True if this content would be handed to the module's firmware scroll (no USB traffic, no CPU):
    /// a seam-free downward scroll on a single module without the CRT look.
    /// </summary>
    public static bool CanRunOnModule(SideContent content, LinkMode link)
    {
        if (link == LinkMode.Span || content.CrtLook)
        {
            return false;
        }

        switch (content.Kind)
        {
            case ContentKind.Animation:
                var info = Animations.Find(content.AnimationId);
                var reversed = content.Reverse && info?.ReverseKind != ReverseKind.None;
                return !reversed && info?.Create().ModuleScrollable == true;
            case ContentKind.Text:
                var t = content.Text;
                if (t.Motion != TextMotion.SlideDown || t.Layout == TextLayout.Ticker || TextTokens.HasTokens(t.Value))
                {
                    return false;
                }

                return TextRenderer.Plan(t.Value, t.Layout, t.Font, Frame.Height).LoopLength() == Frame.Height;
            default:
                return false;
        }
    }

    internal static ushort ScrollPeriod(int speed) => (ushort)Math.Clamp(1000 / Math.Max(1, speed), 20, 2000);

    private void BuildOutputs(EngineConfig c, List<Source> reusable)
    {
        BuildOutputMap(c, reusable);
        _sources = [.. _outputs.Values.Select(o => o.Source).Distinct()];
    }

    private void BuildOutputMap(EngineConfig c, List<Source> reusable)
    {
        Source Make(SideContent content, Frame drawing, int w, int h)
        {
            var key = Source.KeyFor(content, w, h);
            var existing = reusable.FirstOrDefault(s => s.Key == key);
            if (existing is not null)
            {
                reusable.Remove(existing); // a source is reused at most once per side mapping
                existing.Update(content, drawing);
                return existing;
            }

            return Source.Create(content, drawing, w, h, _stats, _time.GetUtcNow());
        }

        _outputs.Clear();
        switch (c.Link)
        {
            case LinkMode.Independent:
                _outputs[MatrixSide.Left] = new Output(Make(c.Left, c.LeftDrawing, Frame.Width, Frame.Height), 0, 0);
                _outputs[MatrixSide.Right] = new Output(Make(c.Right, c.RightDrawing, Frame.Width, Frame.Height), 0, 0);
                break;

            case LinkMode.Mirror:
            {
                var s = Make(c.Left, c.LeftDrawing, Frame.Width, Frame.Height);
                _outputs[MatrixSide.Left] = new Output(s, 0, 0);
                _outputs[MatrixSide.Right] = new Output(s, 0, 0);
                break;
            }

            default: // Span
            {
                if (c.Left.Kind == ContentKind.Drawing)
                {
                    // Drawings are made per module; in Span each side keeps its own.
                    var left = c.Left.Clone();
                    var right = c.Left.Clone();
                    _outputs[MatrixSide.Left] = new Output(Make(left, c.LeftDrawing, Frame.Width, Frame.Height), 0, 0);
                    _outputs[MatrixSide.Right] = new Output(Make(right, c.RightDrawing, Frame.Width, Frame.Height), 0, 0);
                    break;
                }

                var tall = c.Left.Kind == ContentKind.Text && c.Left.Text.Layout != TextLayout.Ticker;
                if (tall)
                {
                    var s = Make(c.Left, c.LeftDrawing, Frame.Width, Frame.Height * 2);
                    _outputs[MatrixSide.Left] = new Output(s, 0, 0);
                    _outputs[MatrixSide.Right] = new Output(s, 0, Frame.Height);
                }
                else
                {
                    var gap = c.KeyboardGap ? KeyboardGapColumns : 0;
                    var s = Make(c.Left, c.LeftDrawing, Frame.Width * 2 + gap, Frame.Height);
                    _outputs[MatrixSide.Left] = new Output(s, 0, 0);
                    _outputs[MatrixSide.Right] = new Output(s, Frame.Width + gap, 0);
                }

                break;
            }
        }
    }

    private sealed record Output(Source Source, int Left, int Top);

    private sealed record Transition(DateTimeOffset Start, Frame From);

    private sealed class SideState
    {
        public CrtFilter Crt { get; } = new();

        public bool CrtOn { get; set; }

        public int CrtIntensity { get; set; } = 70;

        public Frame? LastFrame { get; set; }

        public Frame? LastSent { get; set; }

        public bool Offloaded { get; set; }

        public Transition? Transition { get; set; }

        /// <summary>Content or mapping changed: recompute the frame on the next tick.</summary>
        public bool Dirty { get; set; } = true;

        /// <summary>The source and speed the module's firmware scroll was started with.</summary>
        public Source? OffloadSource { get; set; }

        public ushort OffloadPeriod { get; set; }
    }
}

/// <summary>A content source drawing into its own canvas (which may span both modules).</summary>
internal abstract class Source
{
    private double _accumulator;

    protected Source(int width, int height, int speed)
    {
        Canvas = new PixelBuffer(width, height);
        Speed = speed;
    }

    public PixelBuffer Canvas { get; }

    public int Speed { get; protected set; }

    public bool NeedsRender { get; set; } = true;

    /// <summary>Set by the engine each tick: the picture changed during this tick.</summary>
    public bool ChangedThisTick { get; set; }

    /// <summary>Identity: same key = same source (state kept); differs = rebuilt (and a transition may play).</summary>
    public abstract string Key { get; }

    /// <summary>Changes over time on its own.</summary>
    public abstract bool Dynamic { get; }

    public virtual bool ModuleScrollable => false;

    public static string KeyFor(SideContent c, int w, int h) => c.Kind switch
    {
        ContentKind.Animation => $"anim:{Animations.Find(c.AnimationId)?.Id ?? c.AnimationId}:{w}x{h}",
        ContentKind.Text => $"text:{c.Text.Value}|{c.Text.Layout}|{c.Text.Font}|{c.Text.Motion}:{w}x{h}",
        _ => $"drawing:{w}x{h}",
    };

    public static Source Create(SideContent c, Frame drawing, int w, int h, ISystemStats? stats, DateTimeOffset now)
    {
        var info = c.Kind == ContentKind.Animation ? Animations.Find(c.AnimationId) : null;
        return c.Kind switch
        {
            ContentKind.Animation when info is not null => new AnimationSource(info, w, h, c.Speed, c.Reverse),
            ContentKind.Text => new TextSource(c, w, h, stats, now),
            _ => new DrawingSource(drawing, w, h),
        };
    }

    /// <summary>Applies settings that don't change identity (speed, drawing pixels).</summary>
    public abstract void Update(SideContent content, Frame drawing);

    /// <summary>Advances time; returns true if the picture changed.</summary>
    public abstract bool Advance(double seconds, DateTimeOffset now);

    public abstract void Render();

    /// <summary>Time until the picture changes by itself, or null if it won't (only asked while <see cref="Dynamic"/>).</summary>
    public virtual TimeSpan? NextDue(DateTimeOffset now) => null;

    /// <summary>Time until the next whole step at the current speed.</summary>
    protected TimeSpan? StepDue() =>
        Speed <= 0 ? null : TimeSpan.FromSeconds(Math.Max(0, 1 - _accumulator) / Speed);

    /// <summary>Whole steps due after <paramref name="seconds"/> at the current speed (capped to avoid bursts).</summary>
    protected int DueSteps(double seconds)
    {
        _accumulator += seconds * Speed;

        // A timer that fires a hair early still counts: otherwise the step would slip a whole tick.
        var steps = (int)Math.Floor(_accumulator + StepTolerance);
        _accumulator -= steps;
        return Math.Min(steps, 8);
    }

    private const double StepTolerance = 0.05;
}

internal sealed class DrawingSource : Source
{
    public DrawingSource(Frame drawing, int w, int h)
        : base(w, h, 0) => Copy(drawing);

    public override string Key => $"drawing:{Canvas.Width}x{Canvas.Height}";

    public override bool Dynamic => false;

    public override void Update(SideContent content, Frame drawing) => Copy(drawing);

    public override bool Advance(double seconds, DateTimeOffset now) => false;

    public override void Render()
    {
    }

    private void Copy(Frame drawing)
    {
        if (!drawing.Pixels.SequenceEqual(Canvas.Data))
        {
            drawing.Pixels.CopyTo(Canvas.Data);
            NeedsRender = true;
        }
    }
}

internal sealed class AnimationSource : Source
{
    private readonly LedAnimation _animation;
    private readonly string _id;

    public AnimationSource(AnimationInfo info, int w, int h, int speed, bool reverse)
        : base(w, h, speed)
    {
        _id = info.Id;
        _animation = info.Create();
        _animation.Reset(w, h);
        _animation.Reversed = reverse;
    }

    public override string Key => $"anim:{_id}:{Canvas.Width}x{Canvas.Height}";

    public override bool Dynamic => true;

    // The firmware only scrolls downward, so a reversed scroll is played by the app.
    public override bool ModuleScrollable =>
        _animation.ModuleScrollable && !IsReversed && Canvas.Width == Frame.Width && Canvas.Height == Frame.Height;

    private bool IsReversed => _animation.Reversed && _animation.ReverseKind != ReverseKind.None;

    public override void Update(SideContent content, Frame drawing)
    {
        Speed = content.Speed;
        if (_animation.Reversed != content.Reverse)
        {
            _animation.Reversed = content.Reverse;
            NeedsRender = true; // a flip shows at once, even while paused between steps
        }
    }

    public override bool Advance(double seconds, DateTimeOffset now)
    {
        var steps = DueSteps(seconds);
        for (var i = 0; i < steps; i++)
        {
            _animation.Step();
        }

        return steps > 0;
    }

    public override TimeSpan? NextDue(DateTimeOffset now) => StepDue();

    public override void Render() => _animation.Draw(Canvas);
}

internal sealed class TextSource : Source
{
    private readonly TextSettings _settings;
    private readonly ISystemStats? _stats;
    private readonly bool _hasTokens;
    private readonly bool _clockOnly;
    private string _expanded = string.Empty;
    private TextPlan _plan;
    private long _offset;
    private DateTimeOffset _nextTokenCheck;

    public TextSource(SideContent content, int w, int h, ISystemStats? stats, DateTimeOffset now)
        : base(w, h, content.Speed)
    {
        _settings = content.Text.Clone();
        _stats = stats;
        _hasTokens = TextTokens.HasTokens(_settings.Value);
        _clockOnly = TextTokens.OnlyClockTokens(_settings.Value);
        _expanded = Expand(now);
        _plan = Plan();
    }

    public override string Key =>
        $"text:{_settings.Value}|{_settings.Layout}|{_settings.Font}|{_settings.Motion}:{Canvas.Width}x{Canvas.Height}";

    public override bool Dynamic => _settings.Motion != TextMotion.Static || _hasTokens;

    public override bool ModuleScrollable =>
        _settings.Motion == TextMotion.SlideDown && _settings.Layout != TextLayout.Ticker && !_hasTokens &&
        Canvas.Width == Frame.Width && Canvas.Height == Frame.Height && _plan.LoopLength() == Frame.Height;

    public override void Update(SideContent content, Frame drawing) => Speed = content.Speed;

    public override bool Advance(double seconds, DateTimeOffset now)
    {
        var changed = false;
        if (_hasTokens && now >= _nextTokenCheck)
        {
            var expanded = Expand(now);
            if (expanded != _expanded)
            {
                _expanded = expanded;
                _plan = Plan();
                changed = true;
            }
        }

        if (_settings.Motion != TextMotion.Static)
        {
            var steps = DueSteps(seconds);
            _offset += steps;
            changed |= steps > 0;
        }

        return changed;
    }

    public override TimeSpan? NextDue(DateTimeOffset now)
    {
        var step = _settings.Motion != TextMotion.Static ? StepDue() : null;
        if (!_hasTokens)
        {
            return step;
        }

        var tokens = _nextTokenCheck - now;
        if (tokens < TimeSpan.Zero)
        {
            tokens = TimeSpan.Zero;
        }

        return step is { } s && s < tokens ? s : tokens;
    }

    public override void Render() => TextRenderer.Draw(_plan, _settings.Motion, _offset, Canvas);

    /// <summary>Expands the tokens and schedules the next check: the next minute for {time}/{date}, else 1 s.</summary>
    private string Expand(DateTimeOffset now)
    {
        if (_clockOnly)
        {
            var local = now.ToLocalTime();
            var intoMinute = TimeSpan.FromTicks(local.TimeOfDay.Ticks % TimeSpan.TicksPerMinute);
            _nextTokenCheck = now + (TimeSpan.FromMinutes(1) - intoMinute) + TimeSpan.FromMilliseconds(20);
        }
        else
        {
            _nextTokenCheck = now + TimeSpan.FromSeconds(1);
        }

        return TextTokens.Expand(_settings.Value, now.ToLocalTime(), _stats);
    }

    private TextPlan Plan()
    {
        var viewHeight = _settings.Layout == TextLayout.Ticker ? Frame.Height : Canvas.Height;
        return TextRenderer.Plan(_expanded, _settings.Layout, _settings.Font, viewHeight);
    }
}
