using System.Runtime.InteropServices;
using LedMatrix.Core.Animation;

namespace LedMatrix.App.Services;

/// <summary>
/// Battery, CPU and memory percentages for the {battery}, {cpu} and {ram} text tokens. Values are
/// cached for a second, so reading them from the animation clock is cheap.
/// </summary>
internal sealed class SystemStats : ISystemStats
{
    private readonly object _lock = new();
    private DateTime _lastSample = DateTime.MinValue;
    private ulong _lastIdle;
    private ulong _lastTotal;
    private int? _battery;
    private int? _cpu;
    private int? _ram;

    public int? BatteryPercent
    {
        get
        {
            Refresh();
            return _battery;
        }
    }

    public int? CpuPercent
    {
        get
        {
            Refresh();
            return _cpu;
        }
    }

    public int? RamPercent
    {
        get
        {
            Refresh();
            return _ram;
        }
    }

    private void Refresh()
    {
        lock (_lock)
        {
            var now = DateTime.UtcNow;
            if (now - _lastSample < TimeSpan.FromSeconds(1))
            {
                return;
            }

            _lastSample = now;

            if (GetSystemPowerStatus(out var power) && power.BatteryLifePercent <= 100)
            {
                _battery = power.BatteryLifePercent;
            }

            if (GetSystemTimes(out var idle, out var kernel, out var user))
            {
                var idleTicks = idle.ToUInt64();
                var total = kernel.ToUInt64() + user.ToUInt64(); // kernel time includes idle time
                if (_lastTotal != 0 && total > _lastTotal)
                {
                    var busy = 1.0 - (double)(idleTicks - _lastIdle) / (total - _lastTotal);
                    _cpu = (int)Math.Round(Math.Clamp(busy, 0, 1) * 100);
                }

                _lastIdle = idleTicks;
                _lastTotal = total;
            }

            var memory = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
            if (GlobalMemoryStatusEx(ref memory))
            {
                _ram = (int)memory.dwMemoryLoad;
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SYSTEM_POWER_STATUS
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent; // 255 = unknown
        public byte SystemStatusFlag;
        public uint BatteryLifeTime;
        public uint BatteryFullLifeTime;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FILETIME
    {
        public uint Low;
        public uint High;

        public readonly ulong ToUInt64() => ((ulong)High << 32) | Low;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS status);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out FILETIME idle, out FILETIME kernel, out FILETIME user);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX buffer);
}
