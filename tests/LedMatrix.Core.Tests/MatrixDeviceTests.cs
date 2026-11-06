using LedMatrix.Core;
using Cmd = LedMatrix.Core.Protocol.CommandId;

namespace LedMatrix.Core.Tests;

public sealed class MatrixDeviceTests : IDisposable
{
    private readonly FakeTime _time = new();
    private readonly FakeTransport _port = new();

    private MatrixDevice NewDevice() => new("SN123", _port, _time);

    public void Dispose() => _port.Dispose();

    [Fact]
    public void First_awake_pass_reads_version_wakes_and_sends_content()
    {
        _port.EnqueueResponse(0x00, 0x20, 0x00);
        var dev = NewDevice();
        dev.SetFrame(Patterns.Find("Full")!.Create());

        dev.RunOnce();

        Assert.Equal(DeviceStatus.Connected, dev.Status);
        Assert.Equal(new FirmwareVersion(0, 2, 0, false), dev.Version);
        // Animate(false) clears any firmware scroll another tool may have left running.
        Assert.Equal(new List<Protocol.CommandId> { Cmd.Version, Cmd.Sleep, Cmd.Animate, Cmd.Brightness, Cmd.DrawBw }, _port.Ids());
        Assert.Equal(new byte[] { 0x32, 0xAC, 0x03, 0x00 }, _port.Writes[1]);
    }

    [Fact]
    public void While_off_only_sleep_is_sent_and_content_is_restored_on_wake()
    {
        var dev = NewDevice();
        dev.SetAwake(false);
        dev.RunOnce();

        // Opening while off must not send anything that would wake the LEDs (no version query).
        Assert.Equal(new List<Protocol.CommandId> { Cmd.Sleep }, _port.Ids());
        Assert.Equal(1, _port.Writes[0][3]);
        Assert.Equal(DeviceStatus.Asleep, dev.Status);

        _port.Writes.Clear();
        dev.SetFrame(Patterns.Find("Gradient")!.Create());
        dev.SetBrightness(42);
        _time.Advance(TimeSpan.FromMinutes(5));
        dev.RunOnce();
        Assert.Empty(_port.Writes); // silence, even past the keep-alive interval

        dev.SetAwake(true);
        dev.RunOnce();
        var ids = _port.Ids();
        Assert.Equal(Cmd.Version, ids[0]);
        Assert.Equal(Cmd.Sleep, ids[1]);
        Assert.Equal(0, _port.Writes[1][3]);
        Assert.Equal(Cmd.Animate, ids[2]);
        Assert.Equal(Cmd.Brightness, ids[3]);
        Assert.Equal(42, _port.Writes[3][3]);
        Assert.Equal(9, ids.Count(i => i == Cmd.StageColumn));
        Assert.Equal(Cmd.FlushColumns, ids[^1]);
    }

    [Fact]
    public void Keep_alive_is_sent_before_the_firmware_idle_timeout()
    {
        var dev = NewDevice();
        dev.RunOnce();
        _port.Writes.Clear();

        _time.Advance(TimeSpan.FromSeconds(10));
        dev.RunOnce();
        Assert.Empty(_port.Writes);

        _time.Advance(MatrixDevice.KeepAliveInterval);
        dev.RunOnce();
        Assert.Equal(new List<Protocol.CommandId> { Cmd.Brightness }, _port.Ids());
        Assert.True(MatrixDevice.KeepAliveInterval < Protocol.FirmwareIdleTimeout);
    }

    [Fact]
    public void Only_the_latest_frame_is_sent()
    {
        var dev = NewDevice();
        dev.RunOnce();
        _port.Writes.Clear();

        dev.SetFrame(Patterns.Find("Full")!.Create());
        dev.SetFrame(Patterns.Find("Checkerboard")!.Create());
        dev.RunOnce();

        Assert.Single(_port.Writes);
        Assert.Equal(Protocol.DrawBlackAndWhite(Patterns.Find("Checkerboard")!.Create()), _port.Writes[0]);
    }

    [Fact]
    public void A_picture_the_module_already_shows_is_not_sent_again_unless_forced()
    {
        var dev = NewDevice();
        dev.SetFrame(Patterns.Find("Full")!.Create());
        dev.RunOnce();
        _port.Writes.Clear();

        dev.SetFrame(Patterns.Find("Full")!.Create());
        dev.RunOnce();
        Assert.Empty(_port.Writes);

        dev.Resync(); // e.g. after resume: everything goes out again
        dev.RunOnce();
        Assert.Contains(Cmd.DrawBw, _port.Ids());
    }

    [Fact]
    public void Greyscale_frames_are_encoded_like_the_reference_commands()
    {
        var dev = NewDevice();
        dev.RunOnce();
        _port.Writes.Clear();

        var gradient = Patterns.Find("Gradient")!.Create();
        dev.SetFrame(gradient);
        dev.RunOnce();
        var expected = Protocol.FrameCommands(gradient);
        Assert.Equal(expected.Count, _port.Writes.Count);
        for (var i = 0; i < expected.Count; i++)
        {
            Assert.Equal(expected[i], _port.Writes[i]);
        }
    }

    [Fact]
    public void New_content_on_a_scrolling_module_stops_redraws_and_restarts_the_scroll()
    {
        var dev = NewDevice();
        dev.SetContent(Patterns.Find("Checkerboard")!.Create(), 125);
        dev.RunOnce();
        _port.Writes.Clear();

        dev.SetContent(Patterns.Find("Full")!.Create(), 125);
        dev.RunOnce();
        Assert.Equal(new List<Protocol.CommandId> { Cmd.Animate, Cmd.DrawBw, Cmd.AnimationPeriod, Cmd.Animate }, _port.Ids());
        Assert.Equal(0, _port.Writes[0][3]);
        Assert.Equal(1, _port.Writes[3][3]);
    }

    [Fact]
    public void The_worker_sleeps_until_needed()
    {
        var dev = NewDevice();
        dev.RunOnce();
        Assert.Equal(MatrixDevice.KeepAliveInterval, dev.NextWait()); // awake: next keep-alive

        dev.SetAwake(false);
        dev.RunOnce();
        Assert.Equal(Timeout.InfiniteTimeSpan, dev.NextWait()); // LEDs off: until something changes
    }

    [Fact]
    public void Firmware_scroll_sends_picture_first_then_period_and_animate()
    {
        var dev = NewDevice();
        dev.RunOnce();
        _port.Writes.Clear();

        dev.SetFrame(Patterns.Find("Checkerboard")!.Create());
        dev.SetScroll(125);
        dev.RunOnce();
        Assert.Equal(new List<Protocol.CommandId> { Cmd.DrawBw, Cmd.AnimationPeriod, Cmd.Animate }, _port.Ids());
        Assert.Equal(new byte[] { 0x32, 0xAC, 0x1C, 125, 0 }, _port.Writes[1]);
        Assert.Equal(1, _port.Writes[2][3]);

        // Going back to app-driven frames stops the scroll before the new picture.
        _port.Writes.Clear();
        dev.SetScroll(null);
        dev.SetFrame(new Frame());
        dev.RunOnce();
        Assert.Equal(new List<Protocol.CommandId> { Cmd.Animate, Cmd.DrawBw }, _port.Ids());
        Assert.Equal(0, _port.Writes[0][3]);
    }

    [Fact]
    public void Port_in_use_reports_busy_and_retries_with_backoff()
    {
        _port.OpenException = new UnauthorizedAccessException();
        var dev = NewDevice();

        dev.RunOnce();
        Assert.Equal(DeviceStatus.PortBusy, dev.Status);
        dev.RunOnce(); // within back-off window: no new attempt
        Assert.Equal(1, _port.OpenCount);

        _port.OpenException = null;
        _time.Advance(TimeSpan.FromSeconds(1));
        dev.RunOnce();
        Assert.Equal(DeviceStatus.Connected, dev.Status);
    }

    [Fact]
    public void Write_failure_disconnects_and_resyncs_after_reconnect()
    {
        var dev = NewDevice();
        dev.RunOnce();

        _port.WriteException = new IOException("unplugged");
        dev.SetBrightness(10);
        dev.RunOnce();
        Assert.Equal(DeviceStatus.Disconnected, dev.Status);
        Assert.False(_port.IsOpen);

        _port.WriteException = null;
        _port.Writes.Clear();
        _time.Advance(TimeSpan.FromSeconds(1));
        dev.RunOnce();
        Assert.Equal(DeviceStatus.Connected, dev.Status);
        Assert.Contains(Cmd.Brightness, _port.Ids());
        Assert.Contains(Cmd.DrawBw, _port.Ids());
    }
}
