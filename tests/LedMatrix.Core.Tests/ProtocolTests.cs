using LedMatrix.Core;

namespace LedMatrix.Core.Tests;

public class ProtocolTests
{
    [Fact]
    public void Commands_start_with_magic_bytes()
    {
        Assert.Equal(new byte[] { 0x32, 0xAC, 0x00, 0x80 }, Protocol.SetBrightness(0x80));
        Assert.Equal(new byte[] { 0x32, 0xAC, 0x03, 0x01 }, Protocol.SetSleep(true));
        Assert.Equal(new byte[] { 0x32, 0xAC, 0x03, 0x00 }, Protocol.SetSleep(false));
        Assert.Equal(new byte[] { 0x32, 0xAC, 0x20 }, Protocol.GetVersion());
        Assert.Equal(new byte[] { 0x32, 0xAC, 0x08 }, Protocol.FlushColumns());
    }

    [Fact]
    public void Percentage_pattern_has_two_parameters_and_is_clamped()
    {
        Assert.Equal(new byte[] { 0x32, 0xAC, 0x01, 0x00, 42 }, Protocol.ShowPercentage(42));
        Assert.Equal(100, Protocol.ShowPercentage(250)[4]);
    }

    [Fact]
    public void StageColumn_is_index_plus_34_values()
    {
        var col = Enumerable.Range(0, Frame.Height).Select(i => (byte)i).ToArray();
        var cmd = Protocol.StageColumn(8, col);

        Assert.Equal(3 + 1 + Frame.Height, cmd.Length);
        Assert.Equal((byte)Protocol.CommandId.StageColumn, cmd[2]);
        Assert.Equal(8, cmd[3]);
        Assert.Equal(col, cmd[4..]);
    }

    [Fact]
    public void StageColumn_rejects_out_of_range_input()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Protocol.StageColumn(9, new byte[Frame.Height]));
        Assert.Throws<ArgumentException>(() => Protocol.StageColumn(0, new byte[33]));
    }

    [Fact]
    public void BlackAndWhite_packing_matches_official_web_tool()
    {
        // Reference algorithm copied from dotmatrixtool app.js: i = col + row * width; vals[i/8] |= 1 << i%8
        var frame = Patterns.Find("Checkerboard")!.Create();
        var expected = new byte[39];
        for (var col = 0; col < 9; col++)
        {
            for (var row = 0; row < 34; row++)
            {
                if (frame[col, row] != 0)
                {
                    var i = col + row * 9;
                    expected[i / 8] |= (byte)(1 << (i % 8));
                }
            }
        }

        Assert.Equal(expected, Protocol.PackBlackAndWhite(frame));
    }

    [Fact]
    public void All_commands_fit_in_one_usb_packet()
    {
        var grey = Patterns.Find("Gradient")!.Create();
        foreach (var cmd in Protocol.FrameCommands(grey).Concat(Protocol.FrameCommands(new Frame())))
        {
            Assert.True(cmd.Length <= Protocol.MaxCommandLength);
        }
    }

    [Fact]
    public void Bw_frames_use_one_command_and_greyscale_frames_use_columns_plus_flush()
    {
        var bw = Patterns.Find("Full")!.Create();
        var bwCmds = Protocol.FrameCommands(bw);
        Assert.Single(bwCmds);
        Assert.Equal((byte)Protocol.CommandId.DrawBw, bwCmds[0][2]);
        Assert.Equal(3 + 39, bwCmds[0].Length);

        var grey = Patterns.Find("Gradient")!.Create();
        var greyCmds = Protocol.FrameCommands(grey);
        Assert.Equal(10, greyCmds.Count);
        Assert.Equal((byte)Protocol.CommandId.FlushColumns, greyCmds[^1][2]);
    }

    [Fact]
    public void Version_response_is_decoded()
    {
        var v = Protocol.ParseVersion(new byte[] { 0x00, 0x2A, 0x01 });
        Assert.Equal(new FirmwareVersion(0, 2, 10, true), v);
        Assert.Equal("0.2.10 (pre-release)", v!.Value.ToString());
        Assert.Null(Protocol.ParseVersion(new byte[] { 1 }));
    }
}
