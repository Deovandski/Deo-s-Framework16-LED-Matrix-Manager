namespace LedMatrix.Core.Animation;

/// <summary>Result of laying out a piece of text.</summary>
public sealed record TextPlan(
    PixelBuffer Bitmap,
    PixelFont Font,
    TextLayout Layout,
    int CharacterCount,
    int? Capacity,
    bool Fits)
{
    /// <summary>Blank pixels between repeats when sliding, chosen by <see cref="TextRenderer.Plan"/>.</summary>
    public int Gap { get; init; } = TextRenderer.DefaultGap;

    /// <summary>Length of the text strip along its direction of travel, in pixels.</summary>
    public int Length => Layout == TextLayout.Ticker ? Bitmap.Width : Bitmap.Height;

    /// <summary>Length of one scrolling loop, in pixels, with the planned gap.</summary>
    public int LoopLength() => LoopLength(Gap);

    /// <summary>Length of one scrolling loop, in pixels, for the given gap.</summary>
    public int LoopLength(int gap) => Length + Math.Max(0, gap);
}

/// <summary>Lays out text with the pixel fonts and renders it (static or scrolled) into a canvas.</summary>
public static class TextRenderer
{
    /// <summary>Gap used when the text is longer than the view (and for tickers).</summary>
    public const int DefaultGap = 6;

    /// <summary>Smallest gap that still reads as a break between repeats.</summary>
    public const int MinimumWrapGap = 3;

    /// <summary>
    /// The gap is automatic. Text that fits in the view gets exactly the leftover space, so one loop
    /// is one screen: it wraps around the edges like a marquee, and a downward slide on one module
    /// can run on the firmware. Longer text gets <see cref="DefaultGap"/>.
    /// </summary>
    public static int AutoGap(int textLength, int? viewLength) =>
        viewLength is { } view && view - textLength >= MinimumWrapGap ? view - textLength : DefaultGap;

    /// <summary>How many characters fit without scrolling in a column of <paramref name="viewHeight"/> rows.</summary>
    public static int Capacity(PixelFont font, TextLayout layout, int viewHeight) => layout switch
    {
        TextLayout.Stacked => (viewHeight + 1) / (font.Height + 1),
        TextLayout.Sideways => (viewHeight + 1) / (font.Width + 1),
        _ => 0,
    };

    /// <param name="viewHeight">34 for one module, 68 when the text continues onto the right module (Span).</param>
    public static TextPlan Plan(string text, TextLayout layout, FontChoice choice, int viewHeight)
    {
        var chars = string.IsNullOrEmpty(text) ? " " : text;
        var count = chars.Length;
        PixelFont font;
        if (choice == FontChoice.Auto)
        {
            font = layout == TextLayout.Ticker || count <= Capacity(PixelFont.Regular, layout, viewHeight)
                ? PixelFont.Regular
                : PixelFont.Tiny;
        }
        else
        {
            font = PixelFont.For(choice);
        }

        int? capacity = layout == TextLayout.Ticker ? null : Capacity(font, layout, viewHeight);
        var fits = capacity is null || count <= capacity;
        var bitmap = Render(chars, font, layout);
        var length = layout == TextLayout.Ticker ? bitmap.Width : bitmap.Height;
        return new TextPlan(bitmap, font, layout, count, capacity, fits)
        {
            Gap = AutoGap(length, layout == TextLayout.Ticker ? null : viewHeight),
        };
    }

    /// <summary>Renders the text as one strip: tall for Stacked/Sideways, wide for Ticker.</summary>
    public static PixelBuffer Render(string text, PixelFont font, TextLayout layout)
    {
        var n = Math.Max(1, text.Length);
        switch (layout)
        {
            case TextLayout.Stacked:
            {
                var b = new PixelBuffer(font.Width, n * (font.Height + 1) - 1);
                for (var i = 0; i < text.Length; i++)
                {
                    for (var y = 0; y < font.Height; y++)
                    {
                        for (var x = 0; x < font.Width; x++)
                        {
                            if (font.IsLit(text[i], x, y))
                            {
                                b[x, i * (font.Height + 1) + y] = 255;
                            }
                        }
                    }
                }

                return b;
            }

            case TextLayout.Sideways:
            {
                // Rotated 90° clockwise: glyph rows become columns (top of the glyph on the right).
                var b = new PixelBuffer(font.Height, n * (font.Width + 1) - 1);
                for (var i = 0; i < text.Length; i++)
                {
                    for (var gy = 0; gy < font.Height; gy++)
                    {
                        for (var gx = 0; gx < font.Width; gx++)
                        {
                            if (font.IsLit(text[i], gx, gy))
                            {
                                b[font.Height - 1 - gy, i * (font.Width + 1) + gx] = 255;
                            }
                        }
                    }
                }

                return b;
            }

            default:
            {
                var b = new PixelBuffer(n * (font.Width + 1) - 1, font.Height);
                for (var i = 0; i < text.Length; i++)
                {
                    for (var y = 0; y < font.Height; y++)
                    {
                        for (var x = 0; x < font.Width; x++)
                        {
                            if (font.IsLit(text[i], x, y))
                            {
                                b[i * (font.Width + 1) + x, y] = 255;
                            }
                        }
                    }
                }

                return b;
            }
        }
    }

    /// <summary>Draws the planned text with its automatic gap.</summary>
    public static void Draw(TextPlan plan, TextMotion motion, long offset, PixelBuffer target) =>
        Draw(plan, motion, plan.Gap, offset, target);

    /// <summary>
    /// Draws the planned text into <paramref name="target"/> at scroll position <paramref name="offset"/>.
    /// Stacked/Sideways are centred horizontally; Ticker is centred vertically. Sliding wraps seamlessly
    /// with <paramref name="gap"/> blank pixels between repeats.
    /// </summary>
    public static void Draw(TextPlan plan, TextMotion motion, int gap, long offset, PixelBuffer target)
    {
        target.Clear();
        var src = plan.Bitmap;
        var loop = plan.LoopLength(gap);

        if (plan.Layout == TextLayout.Ticker)
        {
            var top = (target.Height - src.Height) / 2;
            for (var x = 0; x < target.Width; x++)
            {
                int sx = motion switch
                {
                    TextMotion.SlideLeft => Wrap(x + offset, loop),
                    TextMotion.SlideRight => Wrap(x - offset, loop),
                    _ => x - Math.Max(0, (target.Width - src.Width) / 2),
                };
                if (sx < 0 || sx >= src.Width)
                {
                    continue;
                }

                for (var y = 0; y < src.Height; y++)
                {
                    if (top + y >= 0 && top + y < target.Height)
                    {
                        target[x, top + y] = src[sx, y];
                    }
                }
            }

            return;
        }

        var left = (target.Width - src.Width) / 2;
        for (var y = 0; y < target.Height; y++)
        {
            int sy = motion switch
            {
                TextMotion.SlideUp => Wrap(y + offset, loop),
                TextMotion.SlideDown => Wrap(y - offset, loop),
                _ => plan.Fits ? y - (target.Height - src.Height) / 2 : y,
            };
            if (sy < 0 || sy >= src.Height)
            {
                continue;
            }

            for (var x = 0; x < src.Width; x++)
            {
                if (left + x >= 0 && left + x < target.Width)
                {
                    target[left + x, y] = src[x, sy];
                }
            }
        }
    }

    private static int Wrap(long value, int length) => (int)(((value % length) + length) % length);
}
