// Renders showcase animations with LedMatrix.Core into simple binary files that tools/make_gifs.py
// turns into the README GIFs. Usage: dotnet run --project tools/FrameDump -- <output folder>
//
// File format (little-endian): int32 width, int32 height, int32 frameCount, int32 delayMs,
// int32 layout (0 = one module, 1 = two modules edge to edge, 2 = two modules with keyboard gap),
// then frameCount * width * height brightness bytes.

using LedMatrix.Core;
using LedMatrix.Core.Animation;

var outDir = args.Length > 0 ? args[0] : "frames";
Directory.CreateDirectory(outDir);

// Looping animations render exactly one loop, so the GIF repeats without a jump.
Single("rain", Animation("rain", 9, 34, 72));
Single("fire", Animation("fire", 9, 34, 72));
Single("helix", Animation("helix", 9, 34));
Single("plasma", Animation("plasma", 9, 34));
Single("heartbeat", Animation("heartbeat", 9, 34));
Single("text", Text("HELLO DEO", TextLayout.Stacked, TextMotion.SlideUp));
Single("crt", Crt(Animation("helix", 9, 34)));
Single("power", PowerOffOn());
Write("span-ball", Animation("ball", 18, 34, 120), 18, layout: 1);
Write("span-ticker", Ticker("LED MATRIX MANAGER", 24), 24, layout: 2);

Console.WriteLine($"Wrote showcase frames to {Path.GetFullPath(outDir)}");

void Single(string name, List<byte[]> frames) => Write(name, frames, 9, layout: 0);

void Write(string name, List<byte[]> frames, int width, int layout, int delayMs = 90)
{
    using var w = new BinaryWriter(File.Create(Path.Combine(outDir, name + ".bin")));
    w.Write(width);
    w.Write(34);
    w.Write(frames.Count);
    w.Write(delayMs);
    w.Write(layout);
    foreach (var f in frames)
    {
        w.Write(f);
    }
}

static List<byte[]> Animation(string id, int width, int height, int? count = null)
{
    var a = Animations.Find(id)!.Create();
    a.Reset(width, height, seed: 39);
    var buffer = new PixelBuffer(width, height);
    var frames = new List<byte[]>();
    var total = count ?? a.LoopSteps ?? 60;
    if (count is not null && a.LoopSteps is null)
    {
        for (var i = 0; i < 40; i++)
        {
            a.Step(); // warm up random animations so the first frame isn't empty
        }
    }

    for (var i = 0; i < total; i++)
    {
        a.Draw(buffer);
        frames.Add((byte[])buffer.Data.Clone());
        a.Step();
    }

    return frames;
}

static List<byte[]> Text(string text, TextLayout layout, TextMotion motion)
{
    const int gap = 6;
    var plan = TextRenderer.Plan(text, layout, FontChoice.Auto, Frame.Height);
    var buffer = new PixelBuffer(Frame.Width, Frame.Height);
    var frames = new List<byte[]>();
    for (var i = 0; i < plan.LoopLength(gap); i++)
    {
        TextRenderer.Draw(plan, motion, gap, i, buffer);
        frames.Add((byte[])buffer.Data.Clone());
    }

    return frames;
}

static List<byte[]> Ticker(string text, int width)
{
    const int gap = 8;
    var plan = TextRenderer.Plan(text, TextLayout.Ticker, FontChoice.Regular, Frame.Height);
    var buffer = new PixelBuffer(width, Frame.Height);
    var frames = new List<byte[]>();
    for (var i = 0; i < plan.LoopLength(gap); i++)
    {
        TextRenderer.Draw(plan, TextMotion.SlideLeft, gap, i, buffer);
        frames.Add((byte[])buffer.Data.Clone());
    }

    return frames;
}

static List<byte[]> Crt(List<byte[]> source)
{
    var crt = new CrtFilter(seed: 39);
    var frames = new List<byte[]>();
    for (var i = 0; i < source.Count * 2; i++)
    {
        var f = crt.Apply(Frame.FromPixels(source[i % source.Count]), i, 75);
        frames.Add(f.Pixels.ToArray());
    }

    return frames;
}

static List<byte[]> PowerOffOn()
{
    var picture = Patterns.Find("Diamond Pattern")!.Create();
    var frames = new List<byte[]>();
    for (var i = 0; i < 10; i++)
    {
        frames.Add(picture.Pixels.ToArray()); // hold
    }

    for (var i = 0; i <= 12; i++)
    {
        frames.Add(CrtPower.Transform(picture, i / 12.0).Pixels.ToArray()); // off
    }

    for (var i = 0; i < 8; i++)
    {
        frames.Add(new byte[Frame.PixelCount]); // dark
    }

    for (var i = 12; i >= 0; i--)
    {
        frames.Add(CrtPower.Transform(picture, i / 12.0).Pixels.ToArray()); // on
    }

    return frames;
}
