namespace LedMatrix.Core.Animation;

/// <summary>
/// "CRT look" for one module: scanlines (odd rows dimmed), a rolling bright bar, phosphor afterglow
/// (moving things leave short trails) and a slight flicker. Stateful because of the afterglow.
/// Time-based, so it looks the same at any frame rate (the engine runs it at 10 fps).
/// </summary>
public sealed class CrtFilter
{
    /// <summary>Rows per second the bright bar rolls down (it then spends a while off-screen).</summary>
    private const double BarRowsPerSecond = 12;

    /// <summary>The flicker level changes this many times a second.</summary>
    private const double FlickerPerSecond = 5;

    /// <summary>Afterglow kept per 50 ms at full intensity.</summary>
    private const double GlowPer50Ms = 0.62;

    private readonly Random _rng;
    private readonly double[] _previous = new double[Frame.PixelCount];
    private bool _hasPrevious;
    private double _lastSeconds;
    private long _flickerSlot = long.MinValue;
    private double _flicker = 1;

    public CrtFilter(int seed = 7) => _rng = new Random(seed);

    public void Reset()
    {
        _hasPrevious = false;
        _flickerSlot = long.MinValue;
    }

    /// <summary>Frame-number form (20 frames per second), kept for tools that render at a fixed rate.</summary>
    public Frame Apply(Frame input, long tick, int intensity) => Apply(input, tick / 20.0, intensity);

    /// <param name="seconds">A clock in seconds (only differences and the value modulo the bar cycle matter).</param>
    /// <param name="intensity">0..100.</param>
    public Frame Apply(Frame input, double seconds, int intensity)
    {
        var k = Math.Clamp(intensity, 0, 100) / 100.0;
        var barPos = seconds * BarRowsPerSecond % 60;

        var slot = (long)Math.Floor(seconds * FlickerPerSecond);
        if (slot != _flickerSlot)
        {
            _flickerSlot = slot;
            _flicker = 1 - k * 0.08 * _rng.NextDouble();
        }

        // Afterglow fades per unit of time, not per frame.
        var dt = _hasPrevious ? Math.Clamp(seconds - _lastSeconds, 0.001, 0.5) : 0.05;
        var glow = Math.Pow(GlowPer50Ms * k, dt / 0.05);
        _lastSeconds = seconds;

        var src = input.Pixels;
        var output = new byte[Frame.PixelCount];
        for (var y = 0; y < Frame.Height; y++)
        {
            var scan = y % 2 == 1 ? 1 - 0.45 * k : 1;
            var distance = Math.Abs(y - barPos);
            var bar = distance < 3 ? 1 + 0.6 * k * (1 - distance / 3) : 1;
            var lift = bar > 1 ? 18 * k : 0;
            var gain = scan * _flicker * bar;
            var row = Frame.Width * y;
            for (var x = 0; x < Frame.Width; x++)
            {
                var i = row + x;
                var v = src[i] * gain + lift;
                if (_hasPrevious)
                {
                    v = Math.Max(v, _previous[i] * glow);
                }

                var b = PixelBuffer.ToByte(v);
                output[i] = b;
                _previous[i] = b;
            }
        }

        _hasPrevious = true;
        return Frame.Wrap(output);
    }
}

/// <summary>Old-TV power off/on: squash to a bright line, shrink to a dot, fade.</summary>
public static class CrtPower
{
    public static readonly TimeSpan PhaseDuration = TimeSpan.FromMilliseconds(450);

    /// <param name="progress">0 = picture fully on, 1 = fully off. Power-on plays this backwards.</param>
    public static Frame Transform(Frame source, double progress)
    {
        var t = Math.Clamp(progress, 0, 1);
        var output = new Frame();
        const double cy = (Frame.Height - 1) / 2.0;
        const double cx = (Frame.Width - 1) / 2.0;
        var midA = (int)Math.Floor(cy);
        var midB = (int)Math.Ceiling(cy);

        if (t < 0.45)
        {
            // Squash vertically into a brightening line.
            var s = Math.Max(0.03, 1 - t / 0.45);
            for (var y = 0; y < Frame.Height; y++)
            {
                var sy = (int)Math.Round(cy + (y - cy) / s);
                if (sy < 0 || sy >= Frame.Height)
                {
                    continue;
                }

                for (var x = 0; x < Frame.Width; x++)
                {
                    output[x, y] = PixelBuffer.ToByte(source[x, sy] * (1 + (1 - s) * 1.5) + (1 - s) * 90);
                }
            }
        }
        else if (t < 0.8)
        {
            // The line shrinks to a dot.
            var half = (1 - (t - 0.45) / 0.35) * (Frame.Width / 2.0);
            for (var x = 0; x < Frame.Width; x++)
            {
                if (Math.Abs(x - cx) <= half)
                {
                    output[x, midA] = 255;
                    output[x, midB] = 255;
                }
            }
        }
        else
        {
            // The dot fades away.
            var a = PixelBuffer.ToByte(255 * (1 - (t - 0.8) / 0.2));
            output[(int)Math.Round(cx), midA] = a;
            output[(int)Math.Round(cx), midB] = a;
        }

        return output;
    }
}
