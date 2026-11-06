using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Input;
using System.Windows.Media;
using Frame = LedMatrix.Core.Frame;

namespace LedMatrix.App.Controls;

/// <summary>
/// A 9x34 LED editor drawn in a single OnRender pass (no per-cell elements, so it stays light).
/// The hover and keyboard-focus outlines live in a separate overlay visual, so moving the mouse
/// doesn't redraw the 306 cells, and a preview frame identical to the shown one is ignored.
/// Left mouse paints with <see cref="BrushValue"/>, right mouse or Ctrl+left erases.
/// Keyboard: arrows move the cursor, Space/Enter toggles, Delete erases.
/// </summary>
public sealed class LedGrid : FrameworkElement
{
    private const double Gap = 3;
    private const double Pad = 10;
    private const double DefaultCell = 14;

    private static readonly Brush BoardBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x14, 0x14, 0x16)));
    private static readonly Pen HoverPen = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(0x7F, 0xB2, 0xFF)), 1.5));
    private static readonly Pen FocusPen = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(0xFF, 0xC8, 0x3D)), 2));
    private static readonly Brush[] Levels = BuildLevels();

    private Frame _frame = new();
    private (int X, int Y)? _hover;
    private (int X, int Y) _cursor = (Frame.Width / 2, Frame.Height / 2);
    private byte _paintValue;
    private bool _painting;
    private bool _readOnly;
    private readonly DrawingVisual _overlay = new();

    public LedGrid()
    {
        Focusable = true;
        FocusVisualStyle = null;
        Cursor = Cursors.Cross;
        SnapsToDevicePixels = true;
        AddVisualChild(_overlay);
    }

    /// <summary>Raised whenever the user changes a pixel (also continuously while dragging).</summary>
    public event EventHandler? FrameEdited;

    /// <summary>The current image. Setting it copies the frame; reading returns the live frame (clone before keeping it).</summary>
    public Frame Frame
    {
        get => _frame;
        set
        {
            if (value.Equals(_frame))
            {
                return; // unchanged: nothing to redraw
            }

            _frame = value.Clone();
            InvalidateVisual();
        }
    }

    /// <summary>Brightness painted by the left mouse button (1-255).</summary>
    public byte BrushValue { get; set; } = 255;

    /// <summary>When true the grid only shows a live preview (animations, text) and can't be drawn on.</summary>
    public bool IsReadOnly
    {
        get => _readOnly;
        set
        {
            _readOnly = value;
            _painting = false;
            Cursor = value ? Cursors.Arrow : Cursors.Cross;
            RenderOverlay();
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var cell = CellFor(availableSize.Width, availableSize.Height);
        return new Size(BoardWidth(cell), BoardHeight(cell));
    }

    protected override Size ArrangeOverride(Size finalSize) => finalSize;

    protected override int VisualChildrenCount => 1;

    protected override Visual GetVisualChild(int index) =>
        index == 0 ? _overlay : throw new ArgumentOutOfRangeException(nameof(index));

    protected override void OnRender(DrawingContext drawingContext)
    {
        var board = Board(out var cell);
        drawingContext.DrawRoundedRectangle(BoardBrush, null, board, 10, 10);
        var radius = cell * 0.3;
        var pixels = _frame.Pixels;

        for (var y = 0; y < Frame.Height; y++)
        {
            for (var x = 0; x < Frame.Width; x++)
            {
                drawingContext.DrawRoundedRectangle(Levels[pixels[x + Frame.Width * y]], null, CellRect(board, cell, x, y), radius, radius);
            }
        }

        RenderOverlay(); // the size may have changed
    }

    /// <summary>Redraws only the hover and focus outlines (drawn above the cells).</summary>
    private void RenderOverlay()
    {
        using var dc = _overlay.RenderOpen();
        var board = Board(out var cell);
        var radius = cell * 0.3;
        if (_hover is { } h)
        {
            dc.DrawRoundedRectangle(null, HoverPen, CellRect(board, cell, h.X, h.Y), radius, radius);
        }

        if (IsKeyboardFocused && !_readOnly)
        {
            dc.DrawRoundedRectangle(null, FocusPen, CellRect(board, cell, _cursor.X, _cursor.Y), radius, radius);
        }
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new FrameworkElementAutomationPeer(this);

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        if (_readOnly || !HitTest(e.GetPosition(this), out var x, out var y))
        {
            return;
        }

        var erase = e.ChangedButton == MouseButton.Right ||
                    (e.ChangedButton == MouseButton.Left && Keyboard.Modifiers.HasFlag(ModifierKeys.Control));
        if (!erase && e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        _paintValue = erase ? (byte)0 : Math.Max(BrushValue, (byte)1);
        _painting = true;
        _cursor = (x, y);
        CaptureMouse();
        Paint(x, y);
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        (int, int)? hover = !_readOnly && HitTest(e.GetPosition(this), out var x, out var y) ? (x, y) : null;
        if (hover != _hover)
        {
            _hover = hover;
            RenderOverlay();
        }

        if (_painting && hover is { } h)
        {
            Paint(h.Item1, h.Item2);
        }
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        base.OnMouseUp(e);
        if (_painting)
        {
            _painting = false;
            ReleaseMouseCapture();
            e.Handled = true;
        }
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        _painting = false;
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        if (_hover is not null)
        {
            _hover = null;
            RenderOverlay();
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (_readOnly)
        {
            return;
        }

        var (x, y) = _cursor;
        switch (e.Key)
        {
            case Key.Left: x = Math.Max(0, x - 1); break;
            case Key.Right: x = Math.Min(Frame.Width - 1, x + 1); break;
            case Key.Up: y = Math.Max(0, y - 1); break;
            case Key.Down: y = Math.Min(Frame.Height - 1, y + 1); break;
            case Key.Space:
            case Key.Enter:
                _paintValue = _frame[x, y] == 0 ? Math.Max(BrushValue, (byte)1) : (byte)0;
                Paint(x, y);
                break;
            case Key.Delete:
            case Key.Back:
                _paintValue = 0;
                Paint(x, y);
                break;
            default:
                return;
        }

        _cursor = (x, y);
        RenderOverlay();
        e.Handled = true;
    }

    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnGotKeyboardFocus(e);
        RenderOverlay();
    }

    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnLostKeyboardFocus(e);
        RenderOverlay();
    }

    private void Paint(int x, int y)
    {
        if (_frame[x, y] == _paintValue)
        {
            return;
        }

        _frame[x, y] = _paintValue;
        InvalidateVisual();
        FrameEdited?.Invoke(this, EventArgs.Empty);
    }

    private static double BoardWidth(double cell) => 2 * Pad + Frame.Width * cell + (Frame.Width - 1) * Gap;

    private static double BoardHeight(double cell) => 2 * Pad + Frame.Height * cell + (Frame.Height - 1) * Gap;

    private static double CellFor(double width, double height)
    {
        var byWidth = double.IsInfinity(width) ? double.PositiveInfinity : (width - 2 * Pad - (Frame.Width - 1) * Gap) / Frame.Width;
        var byHeight = double.IsInfinity(height) ? double.PositiveInfinity : (height - 2 * Pad - (Frame.Height - 1) * Gap) / Frame.Height;
        var cell = Math.Min(byWidth, byHeight);
        return double.IsInfinity(cell) ? DefaultCell : Math.Max(3, cell);
    }

    private Rect Board(out double cell)
    {
        cell = CellFor(ActualWidth, ActualHeight);
        var w = BoardWidth(cell);
        var h = BoardHeight(cell);
        return new Rect(Math.Max(0, (ActualWidth - w) / 2), Math.Max(0, (ActualHeight - h) / 2), w, h);
    }

    private static Rect CellRect(Rect board, double cell, int x, int y) =>
        new(board.X + Pad + x * (cell + Gap), board.Y + Pad + y * (cell + Gap), cell, cell);

    private bool HitTest(Point p, out int x, out int y)
    {
        var board = Board(out var cell);
        x = (int)Math.Floor((p.X - board.X - Pad + Gap / 2) / (cell + Gap));
        y = (int)Math.Floor((p.Y - board.Y - Pad + Gap / 2) / (cell + Gap));
        return Frame.InBounds(x, y);
    }

    private static Brush[] BuildLevels()
    {
        // 0 = unlit LED (dark grey), 255 = full white; linear in between like the LED PWM.
        var off = Color.FromRgb(0x33, 0x33, 0x36);
        var dim = Color.FromRgb(0x55, 0x55, 0x58);
        var levels = new Brush[256];
        levels[0] = Freeze(new SolidColorBrush(off));
        for (var v = 1; v < 256; v++)
        {
            var t = v / 255.0;
            byte Lerp(byte a) => (byte)Math.Round(a + (255 - a) * t);
            levels[v] = Freeze(new SolidColorBrush(Color.FromRgb(Lerp(dim.R), Lerp(dim.G), Lerp(dim.B))));
        }

        return levels;
    }

    private static T Freeze<T>(T freezable)
        where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
