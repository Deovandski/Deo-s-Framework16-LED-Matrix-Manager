using System.Globalization;
using System.IO;
using System.IO.Ports;
using System.Security;
using LedMatrix.App.Interop;
using LedMatrix.Core;
using Microsoft.Win32;

namespace LedMatrix.App.Services;

/// <param name="Id">Stable identity used for Left/Right roles and names: the USB slot (see below).</param>
/// <param name="PortName">COM port, e.g. COM4.</param>
/// <param name="InstanceId">Windows' device instance name (the USB serial, or a generated one).</param>
internal sealed record DiscoveredModule(string Id, string PortName, string InstanceId);

/// <summary>
/// Finds LED Matrix modules (USB VID 0x32AC / PID 0x0020) and their COM ports without WMI:
/// Windows records each USB serial device's COM name under
/// HKLM\SYSTEM\CurrentControlSet\Enum\USB\VID_32AC&amp;PID_0020\&lt;instance&gt;\Device Parameters\PortName
/// (readable without admin). Stale entries for unplugged modules are filtered out by
/// intersecting with the COM ports that are present right now.
///
/// Modules are identified by the USB slot they sit in, not by serial number: modules that were
/// never given a serial all report the firmware default (FRAKDEBZ0100000000), so Windows names
/// whichever starts first after the serial and the other after its port, and that can swap
/// between boots. The slot ("Port_#0002.Hub_#0004", from Plug and Play) never changes.
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

            foreach (var instance in root.GetSubKeyNames())
            {
                using var parameters = root.OpenSubKey(instance + @"\Device Parameters");
                if (parameters?.GetValue("PortName") is string port && present.Contains(port) && usedPorts.Add(port))
                {
                    var slot = SlotOf(instance);
                    found.Add(new DiscoveredModule(slot is null ? instance : "usb:" + slot, port, instance));
                    Log.Debug($"Module {instance} on {port}, slot {slot ?? "unknown"}");
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

    /// <summary>The USB slot of a connected module ("Port_#0002.Hub_#0004"), or null if Windows can't say.</summary>
    private static string? SlotOf(string instance)
    {
        try
        {
            var id = $@"USB\VID_{Protocol.FrameworkVid:X4}&PID_{Protocol.LedMatrixPid:X4}\{instance}";
            if (NativeMethods.CM_Locate_DevNodeW(out var node, id, 0) != NativeMethods.CR_SUCCESS)
            {
                return null; // not connected right now
            }

            var buffer = new char[256];
            var bytes = (uint)(buffer.Length * sizeof(char));
            if (NativeMethods.CM_Get_DevNode_Registry_PropertyW(
                    node, NativeMethods.CM_DRP_LOCATION_INFORMATION, out _, buffer, ref bytes, 0) != NativeMethods.CR_SUCCESS)
            {
                return null;
            }

            var text = new string(buffer, 0, (int)Math.Min(bytes / sizeof(char), (uint)buffer.Length)).TrimEnd('\0').Trim();
            return text.Length > 0 ? text : null;
        }
        catch (Exception ex) when (ex is EntryPointNotFoundException or DllNotFoundException)
        {
            return null;
        }
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
