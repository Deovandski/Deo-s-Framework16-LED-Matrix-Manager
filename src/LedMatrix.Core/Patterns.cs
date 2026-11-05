namespace LedMatrix.Core;

public sealed record PatternDefinition(string Name, Func<Frame> Create)
{
    public override string ToString() => Name;
}

/// <summary>
/// Built-in patterns. The black/white ones match the official web tool (ledmatrix.frame.work);
/// the greyscale ones are rendered app-side so the editor always shows exactly what is on the LEDs.
/// </summary>
public static class Patterns
{
    private const byte On = 255;
    private const byte Off = 0;

    public static IReadOnlyList<PatternDefinition> All { get; } = Build();

    public static PatternDefinition? Find(string name) =>
        All.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

    private static PatternDefinition Bw(string name, Func<int, int, bool> lit) =>
        new(name, () => Frame.Generate((x, y) => lit(x, y) ? On : Off));

    private static List<PatternDefinition> Build()
    {
        string[] multiples = ["", "", "Double ", "Triple ", "Quad "];
        var list = new List<PatternDefinition>
        {
            Bw("Blank", (_, _) => false),
            Bw("Full", (_, _) => true),
        };

        for (var n = 1; n <= 4; n++)
        {
            var k = n;
            list.Add(Bw($"{multiples[k]}Checkerboard", (col, row) =>
                !((row % (2 * k) < k) ? (col + k) % (2 * k) < k : col % (2 * k) < k)));
        }

        for (var n = 1; n <= 4; n++)
        {
            var k = n;
            list.Add(Bw($"{multiples[k]}Diagonal Lines", (col, row) => (row + col) / k % 2 != 0));
        }

        list.Add(Bw("Every 2nd Row", (_, row) => row % 2 == 0));
        list.Add(Bw("Every 3rd Row", (_, row) => row % 3 == 0));
        list.Add(Bw("Every 2nd Col", (col, _) => col % 2 == 0));
        list.Add(Bw("Every 3rd Col", (col, _) => col % 3 == 0));
        list.Add(Bw("Diamond Pattern", (col, row) =>
        {
            const int size = 5, repeatHeight = 10;
            const int centerCol = Frame.Width / 2, centerRow = repeatHeight / 2;
            return Math.Abs(row % repeatHeight - centerRow) + Math.Abs(col - centerCol) >= size;
        }));
        list.Add(new PatternDefinition("Scatter 50%", Scatter));

        // Greyscale
        list.Add(new PatternDefinition("Gradient", () =>
            Frame.Generate((_, y) => (byte)Math.Round(255.0 * y / (Frame.Height - 1)))));
        list.Add(new PatternDefinition("Double Gradient", () =>
            Frame.Generate((_, y) =>
            {
                var mid = (Frame.Height - 1) / 2.0;
                return (byte)Math.Round(255.0 * (1 - Math.Abs(y - mid) / mid));
            })));
        list.Add(new PatternDefinition("ZigZag", () =>
            Frame.Generate((x, y) =>
            {
                // A 1px zig-zag line bouncing between the left and right edge.
                var period = (Frame.Width - 1) * 2;
                var pos = y % period;
                var lineX = pos < Frame.Width ? pos : period - pos;
                return x == lineX ? On : Off;
            })));

        foreach (var pct in new[] { 25, 50, 75, 100 })
        {
            list.Add(new PatternDefinition($"Percentage {pct}%", () => Percentage(pct)));
        }

        return list;
    }

    /// <summary>Fills the given percentage of the matrix from the bottom up (progress-bar style).</summary>
    public static Frame Percentage(int percent)
    {
        percent = Math.Clamp(percent, 0, 100);
        var litRows = (int)Math.Round(Frame.Height * percent / 100.0);
        return Frame.Generate((_, y) => y >= Frame.Height - litRows ? On : Off);
    }

    private static Frame Scatter()
    {
        var f = new Frame();
        var target = Frame.PixelCount / 2;
        var placed = 0;
        while (placed < target)
        {
            var x = Random.Shared.Next(Frame.Width);
            var y = Random.Shared.Next(Frame.Height);
            if (f[x, y] == Off)
            {
                f[x, y] = On;
                placed++;
            }
        }

        return f;
    }
}
