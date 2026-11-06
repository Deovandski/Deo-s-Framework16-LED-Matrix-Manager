namespace LedMatrix.Core.Animation;

/// <summary>
/// "CRT look" for one module: scanlines (odd rows dimmed), a rolling bright bar, phosphor afterglow
/// (moving things leave short trails) and a slight flicker. Stateful because of the afterglow.
/// </summary>
public sealed class CrtFilter
{
    private readonly Random _rng;
    private byte[]? _previous;

    public CrtFilter(int seed = 7) => _rng = new Random(seed);

    public void Reset() => _previous = null;

    /// <param name="intensity">0..100.</param>
    public Frame Apply(Frame input, long tick, int intensity)
    {
        var k = Math.Clamp(intensity, 0, 100) / 100.0;
        var barPos = tick * 0.6 % 60; // rolls down, then spends a while off-screen
        var flicker = 1 - k * 0.08 * _rng.NextDouble();
        var output = new Frame();
        var src = input.Pixels;
        var next = new byte[Frame.PixelCount];

        for (var y = 0; y < Frame.Height; y++)
        {
            var scan = y % 2 == 1 ? 1 - 0.45 * k : 1;
            var distance = Math.Abs(y - barPos);
            var bar = distance < 3 ? 1 + 0.6 * k * (1 - distance / 3) : 1;
            for (var x = 0; x < Frame.Width; x++)
            {
                var i = x + Frame.Width * y;
                var v = src[i] * scan * flicker * bar + (bar > 1 ? 18 * k : 0);
                if (_previous is not null)
                {
                    v = Math.Max(v, _previous[i] * 0.62 * k);
                }

                next[i] = PixelBuffer.ToByte(v);
                output[x, y] = next[i];
            }
        }

        _previous = next;
        return output;
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
