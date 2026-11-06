namespace LedMatrix.Core.Animation;

/// <summary>How an animation plays when <see cref="LedAnimation.Reversed"/> is on.</summary>
public enum ReverseKind
{
    /// <summary>Direction doesn't mean anything for it (bouncing ball, Game of Life, breathing).</summary>
    None,

    /// <summary>Time runs backwards: the same pictures in the opposite order (still loops exactly).</summary>
    Time,

    /// <summary>The picture is flipped upside down, so rain rises and the rolling bar goes up.</summary>
    Flip,
}

/// <summary>
/// A built-in animation. Animations advance in whole <em>steps</em> (the speed setting is steps per
/// second) and draw onto a canvas of any size, so they work on one module, mirrored, or spanned.
/// Procedural animations are pure functions of (x, y, step) and loop exactly after <see cref="LoopSteps"/>.
/// </summary>
public abstract class LedAnimation
{
    public abstract string Id { get; }

    public abstract string Name { get; }

    public abstract string Description { get; }

    /// <summary>Steps until the animation repeats exactly, or null for continuous (random) ones.</summary>
    public virtual int? LoopSteps => null;

    /// <summary>
    /// True when each step equals the previous image rotated down by one row and the image loops every
    /// 34 rows, so the module's own firmware scroll can play it with no USB traffic.
    /// </summary>
    public virtual bool ModuleScrollable => false;

    /// <summary>What "Reverse direction" does for this animation.</summary>
    public virtual ReverseKind ReverseKind => ReverseKind.Flip;

    /// <summary>Plays in the opposite direction. Can change while running without a jump.</summary>
    public bool Reversed { get; set; }

    protected int Width { get; private set; } = Frame.Width;

    protected int Height { get; private set; } = Frame.Height;

    protected Random Rng { get; private set; } = new(1);

    /// <summary>Current step.</summary>
    protected long T { get; private set; }

    public void Reset(int width, int height, int seed = 1)
    {
        Width = width;
        Height = height;
        Rng = new Random(seed);
        T = 0;
        OnReset();
    }

    public void Step()
    {
        T += Reversed && ReverseKind == ReverseKind.Time ? -1 : 1;
        OnStep();
    }

    public void Draw(PixelBuffer target)
    {
        Render(target);
        if (Reversed && ReverseKind == ReverseKind.Flip)
        {
            FlipVertically(target);
        }
    }

    protected abstract void Render(PixelBuffer target);

    protected virtual void OnReset()
    {
    }

    protected virtual void OnStep()
    {
    }

    protected static long Mod(long a, long m) => ((a % m) + m) % m;

    private static void FlipVertically(PixelBuffer b)
    {
        for (var y = 0; y < b.Height / 2; y++)
        {
            var top = b.Data.AsSpan(y * b.Width, b.Width);
            var bottom = b.Data.AsSpan((b.Height - 1 - y) * b.Width, b.Width);
            for (var x = 0; x < b.Width; x++)
            {
                (top[x], bottom[x]) = (bottom[x], top[x]);
            }
        }
    }
}

/// <summary>
/// An animation defined by a formula over (x, y, step, width, height) returning 0..255. Formulas wrap
/// the step into the loop before any trigonometry, so the last frame matches the first bit for bit.
/// </summary>
internal sealed class ProceduralAnimation(
    string id,
    string name,
    string description,
    int loop,
    Func<int, int, long, int, int, double> pixel,
    bool moduleScrollable = false,
    ReverseKind reverse = ReverseKind.Time)
    : LedAnimation
{
    public override string Id => id;

    public override string Name => name;

    public override string Description => description;

    public override int? LoopSteps => loop;

    public override bool ModuleScrollable => moduleScrollable;

    public override ReverseKind ReverseKind => reverse;

    protected override void Render(PixelBuffer target)
    {
        for (var y = 0; y < target.Height; y++)
        {
            for (var x = 0; x < target.Width; x++)
            {
                target.Set(x, y, pixel(x, y, T, target.Width, target.Height));
            }
        }
    }
}

public sealed record AnimationInfo(string Id, string Name, string Description, ReverseKind ReverseKind, Func<LedAnimation> Create)
{
    public override string ToString() => Name;
}

/// <summary>The catalogue of built-in animations.</summary>
public static class Animations
{
    public static IReadOnlyList<AnimationInfo> All { get; } = Build();

    public static AnimationInfo? Find(string id) =>
        All.FirstOrDefault(a => string.Equals(a.Id, id, StringComparison.OrdinalIgnoreCase));

    private static List<AnimationInfo> Build()
    {
        var list = new List<Func<LedAnimation>>
        {
            () => new ProceduralAnimation("stripes", "Marching stripes", "Diagonal stripes sliding down", 4,
                (x, y, t, w, h) => LedAnimation_Mod((long)Math.Floor((x + y - t) / 2.0), 2) == 0 ? 255 : 0),
            () => new ProceduralAnimation("checker", "Checker march", "Checkerboard stepping one cell", 2,
                (x, y, t, w, h) => LedAnimation_Mod(x + y + t, 2) == 0 ? 255 : 0, moduleScrollable: true),
            () => new ProceduralAnimation("chevrons", "Chevrons", "V-shapes flowing upward", 8,
                (x, y, t, w, h) =>
                {
                    var c = (w - 1) / 2.0;
                    var k = (long)Math.Floor((y + Math.Abs(x - c) + t) / 4.0);
                    return LedAnimation_Mod(k, 2) == 0 ? 255 : 0;
                }),
            () => new ProceduralAnimation("diamonds", "Diamonds", "The diamond pattern scrolling down", 10,
                (x, y, t, w, h) =>
                    Math.Abs(LedAnimation_Mod(y - t, 10) - 5) + Math.Abs(LedAnimation_Mod(x, Frame.Width) - 4) >= 5 ? 255 : 0),
            () => new ProceduralAnimation("sine", "Sine wave", "A smooth wave travelling down", 34,
                (x, y, t, w, h) =>
                {
                    var c = (w - 1) / 2.0;
                    var xs = c + (c - 0.5) * Math.Sin(2 * Math.PI * LedAnimation_Mod(y - t, 34) / 34.0);
                    return 255 * Math.Max(0, 1 - Math.Abs(x - xs));
                }),
            () => new ProceduralAnimation("helix", "Helix", "Two strands crossing like DNA", 34,
                (x, y, t, w, h) =>
                {
                    var c = (w - 1) / 2.0;
                    var p = LedAnimation_Mod(y - t, 34);
                    var a = 2 * Math.PI * p / 34.0;
                    var s = Math.Sin(a);
                    var front = Math.Cos(a) > 0;
                    var x1 = c + (c - 0.5) * s;
                    var x2 = c - (c - 0.5) * s;
                    var b1 = (front ? 255 : 110) * Math.Max(0, 1 - Math.Abs(x - x1));
                    var b2 = (front ? 110 : 255) * Math.Max(0, 1 - Math.Abs(x - x2));
                    // Rungs every 4-4-4-5 rows so they repeat exactly every 34 rows.
                    var rung = p % 17 is 0 or 4 or 8 or 12 && x > Math.Min(x1, x2) && x < Math.Max(x1, x2) ? 45 : 0;
                    return Math.Max(Math.Max(b1, b2), rung);
                }),
            () => new RainAnimation(),
            () => new SnowAnimation(),
            () => new ProceduralAnimation("plasma", "Plasma", "Soft shifting light", 64,
                (x, y, t, w, h) =>
                {
                    var tt = 2 * Math.PI * LedAnimation_Mod(t, 64) / 64.0;
                    var v = Math.Sin(x / 2.2 + tt) + Math.Sin(y / 3.5 - tt) + Math.Sin((x + y) / 4.0 + 2 * tt)
                            + Math.Sin(Math.Sqrt(Math.Pow(x - (w - 1) / 2.0, 2) + Math.Pow(y - (h - 1) / 2.0, 2)) / 3 - tt);
                    return (v + 4) / 8 * 255;
                }),
            () => new FireAnimation(),
            () => new EqualizerAnimation(),
            () => new ProceduralAnimation("heartbeat", "Heartbeat", "An ECG trace with a pulse", 34,
                (x, y, t, w, h) =>
                {
                    var c = (w - 1) / 2.0;
                    var p = LedAnimation_Mod(y - t, 34);
                    double ecg = p switch
                    {
                        10 => -2,
                        12 => 3.6,
                        13 => -3.6,
                        14 => 1.2,
                        >= 20 and <= 22 => 1,
                        _ => 0,
                    };
                    var pulse = 120 + 135 * Math.Max(0, Math.Cos(2 * Math.PI * LedAnimation_Mod(t, 34) / 34.0));
                    return pulse * Math.Max(0, 1 - Math.Abs(x - (c + ecg)));
                }),
            () => new BallAnimation(),
            () => new ProceduralAnimation("breathing", "Breathing", "A diamond pattern slowly pulsing", 60,
                (x, y, t, w, h) =>
                {
                    var lit = Math.Abs(y % 10 - 5) + Math.Abs(x % 9 - 4) >= 5;
                    return lit ? 255 * (0.15 + 0.85 * (0.5 - 0.5 * Math.Cos(2 * Math.PI * LedAnimation_Mod(t, 60) / 60.0))) : 0;
                },
                reverse: ReverseKind.None),
            () => new LifeAnimation(),
            () => new NoSignalAnimation(),
        };

        return list.Select(f =>
        {
            var sample = f();
            return new AnimationInfo(sample.Id, sample.Name, sample.Description, sample.ReverseKind, f);
        }).ToList();
    }

    private static long LedAnimation_Mod(long a, long m) => ((a % m) + m) % m;
}

internal sealed class RainAnimation : LedAnimation
{
    private readonly List<(int X, double Y, double Speed)> _drops = [];
    private double[] _glow = [];

    public override string Id => "rain";

    public override string Name => "Digital rain";

    public override string Description => "Falling drops with fading tails";

    protected override void OnReset()
    {
        _drops.Clear();
        _glow = new double[Width * Height];
    }

    protected override void OnStep()
    {
        for (var i = 0; i < _glow.Length; i++)
        {
            _glow[i] *= 0.78;
        }

        if (Rng.NextDouble() < 0.35 * Width / 9.0)
        {
            _drops.Add((Rng.Next(Width), -1, 0.6 + Rng.NextDouble() * 0.9));
        }

        for (var i = 0; i < _drops.Count; i++)
        {
            var d = _drops[i];
            d.Y += d.Speed;
            _drops[i] = d;
            var yy = (int)Math.Floor(d.Y);
            if (yy >= 0 && yy < Height)
            {
                _glow[d.X + Width * yy] = 255;
            }
        }

        _drops.RemoveAll(d => d.Y >= Height + 1);
    }

    protected override void Render(PixelBuffer target)
    {
        for (var i = 0; i < target.Data.Length && i < _glow.Length; i++)
        {
            target.Data[i] = PixelBuffer.ToByte(_glow[i]);
        }
    }
}

internal sealed class SnowAnimation : LedAnimation
{
    private readonly List<(double X, double Y, double Speed, double Phase)> _flakes = [];

    public override string Id => "snow";

    public override string Name => "Snowfall";

    public override string Description => "Slow flakes drifting down";

    protected override void OnReset() => _flakes.Clear();

    protected override void OnStep()
    {
        if (Rng.NextDouble() < 0.18 * Width / 9.0)
        {
            _flakes.Add((Rng.NextDouble() * Width, -1, 0.15 + Rng.NextDouble() * 0.2, Rng.NextDouble() * 6));
        }

        for (var i = 0; i < _flakes.Count; i++)
        {
            var k = _flakes[i];
            k.Y += k.Speed;
            k.X += Math.Sin(k.Y / 3 + k.Phase) * 0.08;
            _flakes[i] = k;
        }

        _flakes.RemoveAll(k => k.Y >= Height);
    }

    protected override void Render(PixelBuffer target)
    {
        target.Clear();
        foreach (var k in _flakes)
        {
            var x = (int)Math.Round(k.X);
            var y = (int)Math.Round(k.Y);
            if (x >= 0 && x < target.Width && y >= 0 && y < target.Height)
            {
                target[x, y] = Math.Max(target[x, y], (byte)200);
            }
        }
    }
}

internal sealed class FireAnimation : LedAnimation
{
    private double[] _heat = [];

    public override string Id => "fire";

    public override string Name => "Fire";

    public override string Description => "Flames rising from the bottom";

    protected override void OnReset() => _heat = new double[Width * Height];

    protected override void OnStep()
    {
        for (var x = 0; x < Width; x++)
        {
            _heat[x + Width * (Height - 1)] = Rng.NextDouble() < 0.6 ? 255 : 120;
        }

        for (var y = 0; y < Height - 1; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                var below = _heat[x + Width * (y + 1)];
                var bl = _heat[Math.Max(0, x - 1) + Width * (y + 1)];
                var br = _heat[Math.Min(Width - 1, x + 1) + Width * (y + 1)];
                _heat[x + Width * y] = Math.Max(0, (below * 2 + bl + br) / 4 - (6 + Rng.NextDouble() * 10));
            }
        }
    }

    protected override void Render(PixelBuffer target)
    {
        for (var i = 0; i < target.Data.Length && i < _heat.Length; i++)
        {
            target.Data[i] = PixelBuffer.ToByte(_heat[i]);
        }
    }
}

internal sealed class EqualizerAnimation : LedAnimation
{
    private double[] _current = [];
    private double[] _target = [];

    public override string Id => "equalizer";

    public override string Name => "Equalizer";

    public override string Description => "Bouncing bars";

    protected override void OnReset()
    {
        _current = new double[Width];
        _target = new double[Width];
    }

    protected override void OnStep()
    {
        if (T % 3 == 0)
        {
            for (var i = 0; i < _target.Length; i++)
            {
                _target[i] = 4 + Rng.Next(Height - 4);
            }
        }

        for (var i = 0; i < _current.Length; i++)
        {
            _current[i] += (_target[i] - _current[i]) * 0.35;
        }
    }

    protected override void Render(PixelBuffer target)
    {
        target.Clear();
        for (var x = 0; x < target.Width && x < _current.Length; x++)
        {
            for (var y = 0; y < target.Height; y++)
            {
                if (target.Height - y <= _current[x])
                {
                    target[x, y] = 255;
                }
            }
        }
    }
}

internal sealed class BallAnimation : LedAnimation
{
    private double _x;
    private double _y;
    private double _vx;
    private double _vy;

    public override ReverseKind ReverseKind => ReverseKind.None;

    public override string Id => "ball";

    public override string Name => "Bouncing ball";

    public override string Description => "Crosses between modules in Span mode";

    protected override void OnReset()
    {
        _x = 2;
        _y = 3;
        _vx = 0.45;
        _vy = 0.7;
    }

    protected override void OnStep()
    {
        _x += _vx;
        _y += _vy;
        if (_x < 0.5 || _x > Width - 1.5)
        {
            _vx = -_vx;
        }

        if (_y < 0.5 || _y > Height - 1.5)
        {
            _vy = -_vy;
        }

        _x = Math.Clamp(_x, 0.5, Width - 1.5);
        _y = Math.Clamp(_y, 0.5, Height - 1.5);
    }

    protected override void Render(PixelBuffer target)
    {
        for (var y = 0; y < target.Height; y++)
        {
            for (var x = 0; x < target.Width; x++)
            {
                target.Set(x, y, 255 * Math.Max(0, 1.4 - Math.Sqrt(Math.Pow(x - _x, 2) + Math.Pow(y - _y, 2))));
            }
        }
    }
}

internal sealed class LifeAnimation : LedAnimation
{
    private bool[] _cells = [];
    private int _stillSteps;

    public override ReverseKind ReverseKind => ReverseKind.None;

    public override string Id => "life";

    public override string Name => "Game of Life";

    public override string Description => "The classic cellular automaton";

    protected override void OnReset() => Seed();

    protected override void OnStep()
    {
        if (T % 3 != 0)
        {
            return;
        }

        var next = new bool[_cells.Length];
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                var n = 0;
                for (var dy = -1; dy <= 1; dy++)
                {
                    for (var dx = -1; dx <= 1; dx++)
                    {
                        if ((dx != 0 || dy != 0) && _cells[(int)Mod(x + dx, Width) + Width * (int)Mod(y + dy, Height)])
                        {
                            n++;
                        }
                    }
                }

                var alive = _cells[x + Width * y];
                next[x + Width * y] = alive ? n is 2 or 3 : n == 3;
            }
        }

        _stillSteps = next.AsSpan().SequenceEqual(_cells) ? _stillSteps + 1 : 0;
        _cells = next;
        if (_stillSteps > 3 || !_cells.Contains(true))
        {
            Seed();
        }
    }

    protected override void Render(PixelBuffer target)
    {
        for (var i = 0; i < target.Data.Length && i < _cells.Length; i++)
        {
            target.Data[i] = _cells[i] ? (byte)255 : (byte)0;
        }
    }

    private void Seed()
    {
        _cells = new bool[Width * Height];
        for (var i = 0; i < _cells.Length; i++)
        {
            _cells[i] = Rng.NextDouble() < 0.3;
        }

        _stillSteps = 0;
    }
}

internal sealed class NoSignalAnimation : LedAnimation
{
    public override string Id => "nosignal";

    public override string Name => "No signal";

    public override string Description => "CRT static with a rolling bar";

    protected override void Render(PixelBuffer target)
    {
        var tear = Rng.NextDouble() < 0.08 ? Rng.Next(target.Height) : -1;
        var roll = T * 0.8 % 50;
        for (var y = 0; y < target.Height; y++)
        {
            for (var x = 0; x < target.Width; x++)
            {
                double v = Rng.NextDouble() < 0.5 ? Rng.Next(170) : 0;
                if (Math.Abs(y - roll) < 2)
                {
                    v = Math.Min(255, v + 80);
                }

                if (tear >= 0 && y >= tear && y < tear + 2)
                {
                    v = x < target.Width / 2 ? 220 : 30;
                }

                target.Set(x, y, v);
            }
        }
    }
}
