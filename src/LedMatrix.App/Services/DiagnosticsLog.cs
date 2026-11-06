using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Threading;
using LedMatrix.Core;
using LedMatrix.Core.Animation;

namespace LedMatrix.App.Services;

/// <summary>
/// The stats half of Debug mode: while switched on, appends one CSV line a minute to a file per day
/// (Logs folder, stats-yyyy-MM-dd.csv) with the app's CPU and memory use, the animation clock's
/// wake-ups, frames and USB traffic per module, and what was on screen. Off by default; costs one
/// wake-up a minute when on, nothing when off. Files older than 14 days are deleted.
/// </summary>
internal sealed class DiagnosticsLog : IDisposable
{
    private const string Header =
        "timestamp,interval_s,cpu_ms,cpu_pct,working_set_mb,private_mb,managed_heap_mb,allocated_kb,gc0,gc1,gc2,threads,handles," +
        "leds_on,off_reason,window_visible,link,left_content,right_content," +
        "engine_wakeups,left_frames,right_frames,scroll_starts,preview_frames," +
        "left_status,left_worker_wakeups,left_commands,left_bytes,left_frames_sent,left_frames_skipped,left_keepalives,left_opens," +
        "right_status,right_worker_wakeups,right_commands,right_bytes,right_frames_sent,right_frames_skipped,right_keepalives,right_opens";

    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan KeepDays = TimeSpan.FromDays(14);

    private readonly AppController _controller;
    private readonly DispatcherTimer _timer;
    private Sample? _last;

    public DiagnosticsLog(AppController controller, Dispatcher dispatcher)
    {
        _controller = controller;
        _timer = new DispatcherTimer(DispatcherPriority.Background, dispatcher) { Interval = Interval };
        _timer.Tick += (_, _) => WriteSample();
    }

    public bool Enabled => _last is not null;

    /// <summary>The file today's lines go to.</summary>
    public static string TodayFile => FileFor(DateTime.Now);

    public void SetEnabled(bool on)
    {
        if (on == Enabled)
        {
            return;
        }

        if (on)
        {
            _last = Take();
            _timer.Start();
            DeleteOldFiles();
            Log.Info($"Diagnostics log on: {TodayFile}");
        }
        else
        {
            WriteSample(); // the partial last interval
            _timer.Stop();
            _last = null;
            Log.Info("Diagnostics log off");
        }
    }

    public void Dispose()
    {
        if (Enabled)
        {
            WriteSample();
        }

        _timer.Stop();
        _last = null;
    }

    private static string FileFor(DateTime day) =>
        Path.Combine(FileLogger.LogDirectory, $"stats-{day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}.csv");

    private static string Describe(SideContent c) => c.Kind switch
    {
        ContentKind.Animation => $"animation:{c.AnimationId}@{c.Speed}{(c.Reverse ? ":reverse" : string.Empty)}{(c.CrtLook ? "+crt" : string.Empty)}",
        ContentKind.Text => $"text:{c.Text.Layout}:{c.Text.Motion}@{c.Speed}{(c.CrtLook ? "+crt" : string.Empty)}",
        _ => $"drawing{(c.CrtLook ? "+crt" : string.Empty)}",
    };

    private static void DeleteOldFiles()
    {
        try
        {
            if (!Directory.Exists(FileLogger.LogDirectory))
            {
                return;
            }

            foreach (var file in Directory.EnumerateFiles(FileLogger.LogDirectory, "stats-*.csv"))
            {
                if (DateTime.Now - File.GetLastWriteTime(file) > KeepDays)
                {
                    File.Delete(file);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Debug($"Could not tidy old diagnostics files: {ex.Message}");
        }
    }

    private Sample Take()
    {
        using var process = Process.GetCurrentProcess();
        return new Sample(
            DateTimeOffset.Now,
            process.TotalProcessorTime,
            process.WorkingSet64,
            process.PrivateMemorySize64,
            process.Threads.Count,
            process.HandleCount,
            GC.GetTotalMemory(forceFullCollection: false),
            GC.GetTotalAllocatedBytes(),
            GC.CollectionCount(0),
            GC.CollectionCount(1),
            GC.CollectionCount(2),
            _controller.Engine.Counters,
            _controller.Devices.Get(MatrixSide.Left),
            _controller.Devices.Get(MatrixSide.Left)?.Counters ?? default,
            _controller.Devices.Get(MatrixSide.Right),
            _controller.Devices.Get(MatrixSide.Right)?.Counters ?? default);
    }

    private void WriteSample()
    {
        if (_last is not { } last)
        {
            return;
        }

        var now = Take();
        _last = now;
        try
        {
            var path = FileFor(now.Time.LocalDateTime);
            Directory.CreateDirectory(FileLogger.LogDirectory);
            var line = new StringBuilder(512);
            if (!File.Exists(path))
            {
                line.AppendLine(Header);
            }

            AppendLine(line, last, now);
            File.AppendAllText(path, line.ToString());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn("Could not write the diagnostics log", ex);
        }
    }

    private void AppendLine(StringBuilder sb, Sample a, Sample b)
    {
        var ic = CultureInfo.InvariantCulture;
        var seconds = Math.Max(0.001, (b.Time - a.Time).TotalSeconds);
        var cpuMs = (b.Cpu - a.Cpu).TotalMilliseconds;
        var cpuPct = cpuMs / (seconds * 1000 * Environment.ProcessorCount) * 100;
        var settings = _controller.Settings;
        var link = settings.Link;
        var right = link == LinkMode.Independent ? settings.RightContent : settings.LeftContent;

        void Add(FormattableString value) => sb.Append(value.ToString(ic)).Append(',');

        Add($"{b.Time:yyyy-MM-ddTHH:mm:sszzz}");
        Add($"{seconds:F1}");
        Add($"{cpuMs:F0}");
        Add($"{cpuPct:F3}");
        Add($"{b.WorkingSet / 1048576.0:F1}");
        Add($"{b.Private / 1048576.0:F1}");
        Add($"{b.ManagedHeap / 1048576.0:F1}");
        Add($"{(b.Allocated - a.Allocated) / 1024.0:F0}");
        Add($"{b.Gc0 - a.Gc0}");
        Add($"{b.Gc1 - a.Gc1}");
        Add($"{b.Gc2 - a.Gc2}");
        Add($"{b.Threads}");
        Add($"{b.Handles}");
        Add($"{(_controller.LedsOn ? 1 : 0)}");
        Add($"{_controller.OffReason}");
        Add($"{(_controller.PreviewEnabled ? 1 : 0)}");
        Add($"{link}");
        Add($"{Describe(settings.LeftContent)}");
        Add($"{Describe(right)}");
        Add($"{b.Engine.TimerTicks - a.Engine.TimerTicks}");
        Add($"{b.Engine.LeftFramesSent - a.Engine.LeftFramesSent}");
        Add($"{b.Engine.RightFramesSent - a.Engine.RightFramesSent}");
        Add($"{b.Engine.ScrollStarts - a.Engine.ScrollStarts}");
        Add($"{b.Engine.PreviewFrames - a.Engine.PreviewFrames}");
        AppendDevice(sb, ic, a.LeftDevice, a.Left, b.LeftDevice, b.Left);
        sb.Append(',');
        AppendDevice(sb, ic, a.RightDevice, a.Right, b.RightDevice, b.Right);
        sb.AppendLine();
    }

    private static void AppendDevice(
        StringBuilder sb, CultureInfo ic, MatrixDevice? beforeDevice, DeviceCounters before, MatrixDevice? device, DeviceCounters now)
    {
        if (device is null)
        {
            sb.Append("absent,,,,,,,");
            return;
        }

        // A module that was re-detected (new device object) starts counting from zero.
        var from = ReferenceEquals(beforeDevice, device) ? before : default;
        sb.Append(string.Join(',',
            device.Status.ToString(),
            (now.WorkerWakeups - from.WorkerWakeups).ToString(ic),
            (now.Commands - from.Commands).ToString(ic),
            (now.Bytes - from.Bytes).ToString(ic),
            (now.FramesSent - from.FramesSent).ToString(ic),
            (now.FramesSkipped - from.FramesSkipped).ToString(ic),
            (now.KeepAlives - from.KeepAlives).ToString(ic),
            (now.Opens - from.Opens).ToString(ic)));
    }

    private sealed record Sample(
        DateTimeOffset Time,
        TimeSpan Cpu,
        long WorkingSet,
        long Private,
        int Threads,
        int Handles,
        long ManagedHeap,
        long Allocated,
        int Gc0,
        int Gc1,
        int Gc2,
        EngineCounters Engine,
        MatrixDevice? LeftDevice,
        DeviceCounters Left,
        MatrixDevice? RightDevice,
        DeviceCounters Right);
}
