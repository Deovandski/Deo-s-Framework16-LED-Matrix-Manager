namespace LedMatrix.Core;

/// <summary>
/// One 9x34 greyscale image for a single LED Matrix module.
/// Values are LED brightness 0 (off) .. 255 (full). x = column (0..8), y = row (0..33, top to bottom).
/// </summary>
public sealed class Frame : IEquatable<Frame>
{
    public const int Width = 9;
    public const int Height = 34;
    public const int PixelCount = Width * Height;

    private readonly byte[] _pixels;

    public Frame() => _pixels = new byte[PixelCount];

    private Frame(byte[] pixels) => _pixels = pixels;

    /// <summary>Raw pixel buffer, row-major (index = x + Width * y).</summary>
    public ReadOnlySpan<byte> Pixels => _pixels;

    public byte this[int x, int y]
    {
        get
        {
            Validate(x, y);
            return _pixels[x + Width * y];
        }
        set
        {
            Validate(x, y);
            _pixels[x + Width * y] = value;
        }
    }

    public static bool InBounds(int x, int y) => x is >= 0 and < Width && y is >= 0 and < Height;

    /// <summary>Takes ownership of <paramref name="pixels"/> without copying (the caller must not change it afterwards).</summary>
    internal static Frame Wrap(byte[] pixels)
    {
        if (pixels.Length != PixelCount)
        {
            throw new ArgumentException($"Expected {PixelCount} pixels, got {pixels.Length}.", nameof(pixels));
        }

        return new Frame(pixels);
    }

    public static Frame FromPixels(ReadOnlySpan<byte> pixels)
    {
        if (pixels.Length != PixelCount)
        {
            throw new ArgumentException($"Expected {PixelCount} pixels, got {pixels.Length}.", nameof(pixels));
        }

        return new Frame(pixels.ToArray());
    }

    /// <summary>Creates a frame from a per-pixel generator.</summary>
    public static Frame Generate(Func<int, int, byte> pixel)
    {
        var f = new Frame();
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                f._pixels[x + Width * y] = pixel(x, y);
            }
        }

        return f;
    }

    public Frame Clone() => new((byte[])_pixels.Clone());

    public void Clear() => Array.Clear(_pixels);

    public void Fill(byte value) => Array.Fill(_pixels, value);

    public void Invert()
    {
        for (var i = 0; i < _pixels.Length; i++)
        {
            _pixels[i] = (byte)(255 - _pixels[i]);
        }
    }

    public void CopyFrom(Frame other) => other._pixels.CopyTo(_pixels, 0);

    /// <summary>Writes the 34 brightness values of column <paramref name="x"/> into <paramref name="destination"/>.</summary>
    public void CopyColumn(int x, Span<byte> destination)
    {
        if (x is < 0 or >= Width)
        {
            throw new ArgumentOutOfRangeException(nameof(x));
        }

        for (var y = 0; y < Height; y++)
        {
            destination[y] = _pixels[x + Width * y];
        }
    }

    /// <summary>The 34 brightness values of one column, top to bottom.</summary>
    public byte[] GetColumn(int x)
    {
        if (x is < 0 or >= Width)
        {
            throw new ArgumentOutOfRangeException(nameof(x));
        }

        var col = new byte[Height];
        for (var y = 0; y < Height; y++)
        {
            col[y] = _pixels[x + Width * y];
        }

        return col;
    }

    /// <summary>True if every pixel is either 0 or 255 (can be sent with the compact DrawBW command).</summary>
    public bool IsBlackAndWhite()
    {
        foreach (var p in _pixels)
        {
            if (p is not 0 and not 255)
            {
                return false;
            }
        }

        return true;
    }

    public string ToBase64() => Convert.ToBase64String(_pixels);

    public static Frame? TryFromBase64(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        try
        {
            var bytes = Convert.FromBase64String(value);
            return bytes.Length == PixelCount ? new Frame(bytes) : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    public bool Equals(Frame? other) => other is not null && _pixels.AsSpan().SequenceEqual(other._pixels);

    public override bool Equals(object? obj) => Equals(obj as Frame);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.AddBytes(_pixels);
        return hash.ToHashCode();
    }

    private static void Validate(int x, int y)
    {
        if (!InBounds(x, y))
        {
            throw new ArgumentOutOfRangeException($"({x},{y}) is outside the {Width}x{Height} matrix.");
        }
    }
}
