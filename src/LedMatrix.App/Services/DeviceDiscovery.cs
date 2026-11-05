using System.Globalization;
using System.IO;
using System.IO.Ports;
using System.Security;
using LedMatrix.Core;
using Microsoft.Win32;

namespace LedMatrix.App.Services;

internal sealed record DiscoveredModule(string SerialNumber, string PortName);

/// <summary>
/// Finds LED Matrix modules (USB VID 0x32AC / PID 0x0020) and their COM ports without WMI:
/// Windows records each USB serial device's COM name under
/// HKLM\SYSTEM\CurrentControlSet\Enum\USB\VID_32AC&amp;PID_0020\&lt;serial&gt;\Device Parameters\PortName
/// (readable without admin). Stale entries for unplugged modules are filtered out by
/// intersecting with the COM ports that are present right now.
/// </summary>
internal static class DeviceDiscovery
{
    private static readonly string EnumKey =
        $@"SYSTEM\CurrentControlSet\Enum\USB\VID_{Protocol.FrameworkVid:X4}&PID_{Protocol.LedMatrixPid:X4}";

    public static IReadOnlyList<DiscoveredModule> Find()
    {
        var present = new HashSet<string>(SafeGetPortNames(), StringComparer.OrdinalIgnoreCase);
        var found = new List<DiscoveredModule>();
        var usedPorts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var root = Registry.LocalMachine.OpenSubKey(EnumKey);
            if (root is null)
            {
                return found;
            }

            foreach (var serial in root.GetSubKeyNames())
            {
                using var parameters = root.OpenSubKey(serial + @"\Device Parameters");
                if (parameters?.GetValue("PortName") is string port && present.Contains(port) && usedPorts.Add(port))
                {
                    found.Add(new DiscoveredModule(serial, port));
                }
            }
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
        {
            Log.Warn("Could not read the USB device registry", ex);
        }

        // Stable order (COM3 before COM10) so first-time Left/Right assignment is predictable.
        return found.OrderBy(m => PortNumber(m.PortName)).ThenBy(m => m.PortName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string[] SafeGetPortNames()
    {
        try
        {
            return SerialPort.GetPortNames();
        }
        catch (Exception ex)
        {
            Log.Warn("Could not list COM ports", ex);
            return [];
        }
    }

    private static int PortNumber(string port) =>
        port.StartsWith("COM", StringComparison.OrdinalIgnoreCase) &&
        int.TryParse(port.AsSpan(3), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : int.MaxValue;
}
