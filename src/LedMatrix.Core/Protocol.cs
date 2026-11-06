namespace LedMatrix.Core;

/// <summary>
/// Encodes commands for the Framework 16 LED Matrix firmware (inputmodule-rs).
/// Every command is: 0x32 0xAC &lt;id&gt; &lt;params...&gt; and must be written as a single
/// write of at most 64 bytes (the firmware parses one command per USB read).
/// See docs/PROTOCOL.md.
/// </summary>
public static class Protocol
{
    public const ushort FrameworkVid = 0x32AC;
    public const ushort LedMatrixPid = 0x0020;
    public const int BaudRate = 115_200;
    public const int ResponseLength = 32;
    public const int MaxCommandLength = 64;

    /// <summary>The firmware sleeps after this long without receiving a command.</summary>
    public static readonly TimeSpan FirmwareIdleTimeout = TimeSpan.FromSeconds(60);

    private const byte Magic0 = 0x32;
    private const byte Magic1 = 0xAC;

    public enum CommandId : byte
    {
        Brightness = 0x00,
        Pattern = 0x01,
        Sleep = 0x03,
        Animate = 0x04,
        DrawBw = 0x06,
        StageColumn = 0x07,
        FlushColumns = 0x08,
        AnimationPeriod = 0x1C,
        Version = 0x20,
    }

    public enum FirmwarePattern : byte
    {
        Percentage = 0x00,
        Gradient = 0x01,
        DoubleGradient = 0x02,
        LotusSideways = 0x03,
        ZigZag = 0x04,
        FullBrightness = 0x05,
        LotusTopDown = 0x07,
    }

    public static byte[] Command(CommandId id, params ReadOnlySpan<byte> parameters)
    {
        if (3 + parameters.Length > MaxCommandLength)
        {
            throw new ArgumentException("Command exceeds 64 bytes.", nameof(parameters));
        }

        var buf = new byte[3 + parameters.Length];
        buf[0] = Magic0;
        buf[1] = Magic1;
        buf[2] = (byte)id;
        parameters.CopyTo(buf.AsSpan(3));
        return buf;
    }

    public static byte[] SetBrightness(byte brightness) => Command(CommandId.Brightness, brightness);

    public static byte[] GetBrightness() => Command(CommandId.Brightness);

    public static byte[] SetSleep(bool sleep) => Command(CommandId.Sleep, sleep ? (byte)1 : (byte)0);

    public static byte[] GetSleep() => Command(CommandId.Sleep);

    public static byte[] SetAnimate(bool animate) => Command(CommandId.Animate, animate ? (byte)1 : (byte)0);

    public static byte[] GetVersion() => Command(CommandId.Version);

    /// <summary>Firmware scroll speed: one row every <paramref name="milliseconds"/> ms (little-endian u16).</summary>
    public static byte[] SetAnimationPeriod(ushort milliseconds) =>
        Command(CommandId.AnimationPeriod, (byte)(milliseconds & 0xFF), (byte)(milliseconds >> 8));

    public static byte[] ShowFirmwarePattern(FirmwarePattern pattern) => Command(CommandId.Pattern, (byte)pattern);

    public static byte[] ShowPercentage(byte percent) =>
        Command(CommandId.Pattern, (byte)FirmwarePattern.Percentage, Math.Min(percent, (byte)100));

    /// <summary>Stage one greyscale column. Must be followed by <see cref="FlushColumns"/>.</summary>
    public static byte[] StageColumn(int x, ReadOnlySpan<byte> column)
    {
        if (x is < 0 or >= Frame.Width)
        {
            throw new ArgumentOutOfRangeException(nameof(x));
        }

        if (column.Length != Frame.Height)
        {
            throw new ArgumentException($"Column must have {Frame.Height} values.", nameof(column));
        }

        Span<byte> p = stackalloc byte[1 + Frame.Height];
        p[0] = (byte)x;
        column.CopyTo(p[1..]);
        return Command(CommandId.StageColumn, p);
    }

    public static byte[] FlushColumns() => Command(CommandId.FlushColumns);

    /// <summary>Packs a frame into the 39-byte DrawBW payload: bit i = x + 9*y, LSB first; any non-zero pixel = on.</summary>
    public static byte[] PackBlackAndWhite(Frame frame)
    {
        var vals = new byte[(Frame.PixelCount + 7) / 8]; // 39
        var px = frame.Pixels;
        for (var i = 0; i < Frame.PixelCount; i++)
        {
            if (px[i] != 0)
            {
                vals[i / 8] |= (byte)(1 << (i % 8));
            }
        }

        return vals;
    }

    public static byte[] DrawBlackAndWhite(Frame frame) => Command(CommandId.DrawBw, PackBlackAndWhite(frame));

    /// <summary>
    /// Commands needed to show a frame. Pure black/white frames use one DrawBW command;
    /// greyscale frames use 9 StageColumn commands and one FlushColumns (no tearing).
    /// </summary>
    public static IReadOnlyList<byte[]> FrameCommands(Frame frame)
    {
        if (frame.IsBlackAndWhite())
        {
            return [DrawBlackAndWhite(frame)];
        }

        var cmds = new List<byte[]>(Frame.Width + 1);
        for (var x = 0; x < Frame.Width; x++)
        {
            cmds.Add(StageColumn(x, frame.GetColumn(x)));
        }

        cmds.Add(FlushColumns());
        return cmds;
    }

    public static FirmwareVersion? ParseVersion(ReadOnlySpan<byte> response)
    {
        if (response.Length < 3)
        {
            return null;
        }

        return new FirmwareVersion(response[0], (response[1] & 0xF0) >> 4, response[1] & 0x0F, response[2] == 1);
    }
}

public readonly record struct FirmwareVersion(int Major, int Minor, int Patch, bool PreRelease)
{
    public override string ToString() => $"{Major}.{Minor}.{Patch}{(PreRelease ? " (pre-release)" : string.Empty)}";
}
