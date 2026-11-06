namespace LedMatrix.Core.Animation;

/// <summary>What a module shows.</summary>
public enum ContentKind
{
    Drawing,
    Animation,
    Text,
}

/// <summary>How the two modules relate.</summary>
public enum LinkMode
{
    /// <summary>Each module has its own content.</summary>
    Independent,

    /// <summary>The Left content is shown on both.</summary>
    Mirror,

    /// <summary>The Left content is laid out across both as one canvas.</summary>
    Span,
}

public enum TextLayout
{
    /// <summary>Upright letters one above another (the default).</summary>
    Stacked,

    /// <summary>Letters rotated 90° clockwise, read top to bottom.</summary>
    Sideways,

    /// <summary>Upright letters moving sideways through the window.</summary>
    Ticker,
}

public enum TextMotion
{
    Static,
    SlideUp,
    SlideDown,
    SlideLeft,
    SlideRight,
}

public sealed class TextSettings
{
    public string Value { get; set; } = "HELLO";

    public TextLayout Layout { get; set; } = TextLayout.Stacked;

    public FontChoice Font { get; set; } = FontChoice.Auto;

    public TextMotion Motion { get; set; } = TextMotion.Static;

    public TextSettings Clone() => (TextSettings)MemberwiseClone();
}

/// <summary>Persisted description of one module's content (not raw frames).</summary>
public sealed class SideContent
{
    /// <summary>Calm default: 8 steps (rows/pixels) per second.</summary>
    public const int DefaultSpeed = 8;

    public ContentKind Kind { get; set; } = ContentKind.Drawing;

    public string AnimationId { get; set; } = "rain";

    /// <summary>Steps per second (1..32).</summary>
    public int Speed { get; set; } = DefaultSpeed;

    /// <summary>Animations only: play in the opposite direction.</summary>
    public bool Reverse { get; set; }

    public TextSettings Text { get; set; } = new();

    public bool CrtLook { get; set; }

    /// <summary>CRT look intensity 0..100.</summary>
    public int CrtIntensity { get; set; } = 70;

    public SideContent Clone()
    {
        var c = (SideContent)MemberwiseClone();
        c.Text = Text.Clone();
        return c;
    }
}
