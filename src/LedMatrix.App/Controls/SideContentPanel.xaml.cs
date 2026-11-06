using System.Windows;
using System.Windows.Controls;
using LedMatrix.Core;
using LedMatrix.Core.Animation;

namespace LedMatrix.App.Controls;

/// <summary>
/// Content settings for one module: Drawing / Animation / Text, speed and CRT look. It only edits a
/// <see cref="SideContent"/>; the main window passes every change on to the app as a small mutation.
/// </summary>
public partial class SideContentPanel : UserControl
{
    private const string PatternPlaceholder = "Fill with a pattern…";

    private static readonly (TextMotion Motion, string Label)[] VerticalMotions =
    [
        (TextMotion.Static, "Static"), (TextMotion.SlideUp, "Slide up"), (TextMotion.SlideDown, "Slide down"),
    ];

    private static readonly (TextMotion Motion, string Label)[] TickerMotions =
    [
        (TextMotion.SlideLeft, "Slide left"), (TextMotion.SlideRight, "Slide right"), (TextMotion.Static, "Static"),
    ];

    private bool _loading = true;
    private SideContent _content = new();
    private LinkMode _link;

    public SideContentPanel()
    {
        InitializeComponent();

        PatternBox.Items.Add(PatternPlaceholder);
        foreach (var p in Patterns.All)
        {
            PatternBox.Items.Add(p.Name);
        }

        PatternBox.SelectedIndex = 0;
        foreach (var a in Animations.All)
        {
            AnimationBox.Items.Add(new ComboBoxItem { Content = a.Name, Tag = a.Id });
        }

        _loading = false;
    }

    /// <summary>Which module this panel edits.</summary>
    public MatrixSide Side { get; set; }

    /// <summary>A change to apply to this module's content.</summary>
    public event EventHandler<Action<SideContent>>? ContentChanged;

    public event EventHandler<PatternDefinition>? PatternChosen;

    public event EventHandler? ClearRequested;

    public event EventHandler? InvertRequested;

    /// <summary>Shows <paramref name="content"/>. <paramref name="following"/> = this module mirrors/spans the left one.</summary>
    public void Load(SideContent content, LinkMode link, bool following)
    {
        _loading = true;
        try
        {
            _content = content.Clone();
            _link = link;

            KindBox.IsEnabled = !following;
            FollowingText.Visibility = following ? Visibility.Visible : Visibility.Collapsed;
            KindBox.SelectedIndex = (int)_content.Kind;

            foreach (ComboBoxItem item in AnimationBox.Items)
            {
                if (string.Equals((string)item.Tag, _content.AnimationId, StringComparison.OrdinalIgnoreCase))
                {
                    AnimationBox.SelectedItem = item;
                }
            }

            if (AnimationBox.SelectedIndex < 0)
            {
                AnimationBox.SelectedIndex = 0;
            }

            TextInput.Text = _content.Text.Value;
            LayoutBox.SelectedIndex = (int)_content.Text.Layout;
            FontBox.SelectedIndex = (int)_content.Text.Font;
            FillMotions();
            SpeedSlider.Value = _content.Speed;
            ReverseCheck.IsChecked = _content.Reverse;
            CrtCheck.IsChecked = _content.CrtLook;
            CrtSlider.Value = _content.CrtIntensity;

            UpdateLayoutAndInfo(following);
        }
        finally
        {
            _loading = false;
        }
    }

    private void Change(Action<SideContent> mutation)
    {
        mutation(_content);
        UpdateLayoutAndInfo(following: !KindBox.IsEnabled);
        ContentChanged?.Invoke(this, mutation);
    }

    private void FillMotions()
    {
        var options = _content.Text.Layout == TextLayout.Ticker ? TickerMotions : VerticalMotions;
        MotionBox.Items.Clear();
        foreach (var (motion, label) in options)
        {
            MotionBox.Items.Add(new ComboBoxItem { Content = label, Tag = motion });
        }

        var index = Array.FindIndex(options, o => o.Motion == _content.Text.Motion);
        MotionBox.SelectedIndex = index >= 0 ? index : 0;
    }

    private void UpdateLayoutAndInfo(bool following)
    {
        var kind = _content.Kind;
        DrawingPanel.Visibility = !following && kind == ContentKind.Drawing ? Visibility.Visible : Visibility.Collapsed;
        AnimationPanel.Visibility = !following && kind == ContentKind.Animation ? Visibility.Visible : Visibility.Collapsed;
        TextPanel.Visibility = !following && kind == ContentKind.Text ? Visibility.Visible : Visibility.Collapsed;
        var moving = kind == ContentKind.Animation || (kind == ContentKind.Text && _content.Text.Motion != TextMotion.Static);
        SpeedRow.Visibility = !following && moving ? Visibility.Visible : Visibility.Collapsed;
        CrtRow.Visibility = following ? Visibility.Collapsed : Visibility.Visible;
        CrtSlider.IsEnabled = _content.CrtLook;

        SpeedValue.Text = $"{_content.Speed} /s";
        var animation = Animations.Find(_content.AnimationId);
        AnimationDescription.Text = animation?.Description ?? string.Empty;
        var reversible = animation?.ReverseKind is not (null or ReverseKind.None);
        ReverseCheck.IsEnabled = reversible;
        ReverseCheck.ToolTip = reversible
            ? "Play the animation the other way: up instead of down, backwards in time"
            : "This animation has no direction to reverse";
        FitText.Text = kind == ContentKind.Text ? DescribeFit() : string.Empty;
        EngineText.Text = following ? string.Empty : DescribeEngine();
    }

    private string DescribeFit()
    {
        var t = _content.Text;
        var spanTall = _link == LinkMode.Span && t.Layout != TextLayout.Ticker;
        var expanded = TextTokens.Expand(t.Value, DateTimeOffset.Now, null);
        var plan = TextRenderer.Plan(expanded, t.Layout, t.Font, spanTall ? LedMatrix.Core.Frame.Height * 2 : LedMatrix.Core.Frame.Height);
        if (t.Layout == TextLayout.Ticker)
        {
            return $"{plan.Font.Name}. Ticker text always moves; one loop is {plan.LoopLength()} px.";
        }

        if (plan.Fits)
        {
            return $"{plan.Font.Name}: fits ({plan.CharacterCount} of {plan.Capacity} characters{(spanTall ? " across both modules" : string.Empty)}).";
        }

        return t.Motion == TextMotion.Static
            ? $"Doesn't fit: {plan.CharacterCount} characters, {plan.Capacity} fit in {plan.Font.Name}. Choose Slide up or Slide down, or shorten the text."
            : $"{plan.Font.Name}, sliding ({plan.CharacterCount} characters).";
    }

    private string DescribeEngine()
    {
        if (AnimationEngine.CanRunOnModule(_content, _link))
        {
            return "Runs on the module itself: 0% CPU.";
        }

        var isStill = _content.Kind == ContentKind.Drawing ||
                      (_content.Kind == ContentKind.Text && _content.Text.Motion == TextMotion.Static && !TextTokens.HasTokens(_content.Text.Value));
        if (isStill && !_content.CrtLook)
        {
            return string.Empty;
        }

        return $"Animated by the app at up to {AnimationEngine.FramesPerSecond} frames per second.";
    }

    private void Kind_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || KindBox.SelectedIndex < 0)
        {
            return;
        }

        var kind = (ContentKind)KindBox.SelectedIndex;
        Change(c => c.Kind = kind);
    }

    private void Pattern_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || PatternBox.SelectedItem is not string name || name == PatternPlaceholder)
        {
            return;
        }

        if (Patterns.Find(name) is { } pattern)
        {
            PatternChosen?.Invoke(this, pattern);
        }

        _loading = true;
        PatternBox.SelectedIndex = 0;
        _loading = false;
    }

    private void Clear_Click(object sender, RoutedEventArgs e) => ClearRequested?.Invoke(this, EventArgs.Empty);

    private void Invert_Click(object sender, RoutedEventArgs e) => InvertRequested?.Invoke(this, EventArgs.Empty);

    private void Animation_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || AnimationBox.SelectedItem is not ComboBoxItem { Tag: string id })
        {
            return;
        }

        Change(c => c.AnimationId = id);
    }

    private void Text_Changed(object sender, TextChangedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        var value = TextInput.Text;
        Change(c => c.Text.Value = value);
    }

    private void TextOption_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || LayoutBox.SelectedIndex < 0 || FontBox.SelectedIndex < 0)
        {
            return;
        }

        var layout = (TextLayout)LayoutBox.SelectedIndex;
        var font = (FontChoice)FontBox.SelectedIndex;
        if (layout != _content.Text.Layout)
        {
            // Different layouts offer different motions; keep it moving if it was.
            var wasMoving = _content.Text.Motion != TextMotion.Static;
            var motionForLayout = layout == TextLayout.Ticker
                ? (wasMoving ? TextMotion.SlideLeft : TextMotion.Static)
                : (wasMoving ? TextMotion.SlideUp : TextMotion.Static);
            _loading = true;
            _content.Text.Layout = layout;
            _content.Text.Motion = motionForLayout;
            FillMotions();
            _loading = false;
            Change(c =>
            {
                c.Text.Layout = layout;
                c.Text.Font = font;
                c.Text.Motion = motionForLayout;
            });
            return;
        }

        var motion = MotionBox.SelectedItem is ComboBoxItem { Tag: TextMotion m } ? m : TextMotion.Static;
        Change(c =>
        {
            c.Text.Layout = layout;
            c.Text.Font = font;
            c.Text.Motion = motion;
        });
    }

    private void Reverse_Click(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        var on = ReverseCheck.IsChecked == true;
        Change(c => c.Reverse = on);
    }

    private void Speed_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading)
        {
            return;
        }

        var speed = (int)Math.Round(e.NewValue);
        Change(c => c.Speed = speed);
    }

    private void Crt_Click(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        var on = CrtCheck.IsChecked == true;
        Change(c => c.CrtLook = on);
    }

    private void CrtIntensity_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading)
        {
            return;
        }

        var amount = (int)Math.Round(e.NewValue);
        Change(c => c.CrtIntensity = amount);
    }
}
