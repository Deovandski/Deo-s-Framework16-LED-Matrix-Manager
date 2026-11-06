namespace LedMatrix.Core.Animation;

/// <summary>
/// A greyscale canvas of any size (a single module is 9x34; Span mode uses 18x34, 24x34 with the
/// keyboard gap, or 9x68 for tall text). Values are LED brightness 0..255, row-major.
/// </summary>
public sealed class PixelBuffer
{
    public PixelBuffer(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        Width = width;
        Height = height;
        Data = new byte[width * height];
    }

    public int Width { get; }

    public int Height { get; }

    /// <summary>Row-major pixel data (index = x + Width * y).</summary>
    public byte[] Data { get; }

    public byte this[int x, int y]
    {
        get => Data[x + Width * y];
        set => Data[x + Width * y] = value;
    }

    public void Clear() => Array.Clear(Data);

    /// <summary>Sets a pixel from a 0..255 double, clamped and rounded.</summary>
    public void Set(int x, int y, double value) => Data[x + Width * y] = ToByte(value);

    public static byte ToByte(double value) => (byte)Math.Clamp((int)Math.Round(value), 0, 255);

    /// <summary>Copies a 9x34 window starting at (left, top) into a module frame.</summary>
    public Frame ToFrame(int left = 0, int top = 0)
    {
        if (left >= 0 && top >= 0 && left + Frame.Width <= Width && top + Frame.Height <= Height)
        {
            // Common case: the window lies inside the canvas, so copy whole rows.
            var pixels = new byte[Frame.PixelCount];
            for (var y = 0; y < Frame.Height; y++)
            {
                Data.AsSpan(left + Width * (top + y), Frame.Width).CopyTo(pixels.AsSpan(Frame.Width * y, Frame.Width));
            }

            return Frame.Wrap(pixels);
        }

        var f = new Frame();
        for (var y = 0; y < Frame.Height; y++)
        {
            for (var x = 0; x < Frame.Width; x++)
            {
                var sx = left + x;
                var sy = top + y;
                if (sx >= 0 && sx < Width && sy >= 0 && sy < Height)
                {
                    f[x, y] = Data[sx + Width * sy];
                }
            }
        }

        return f;
    }

    public static PixelBuffer FromFrame(Frame frame)
    {
        var b = new PixelBuffer(Frame.Width, Frame.Height);
        frame.Pixels.CopyTo(b.Data);
        return b;
    }

    public PixelBuffer Clone()
    {
        var b = new PixelBuffer(Width, Height);
        Data.CopyTo(b.Data, 0);
        return b;
    }
}
