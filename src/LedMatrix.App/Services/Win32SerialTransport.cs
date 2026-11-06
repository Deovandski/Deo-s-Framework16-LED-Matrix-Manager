using System.IO;
using System.Runtime.InteropServices;
using LedMatrix.App.Interop;
using LedMatrix.Core;
using Microsoft.Win32.SafeHandles;

namespace LedMatrix.App.Services;

/// <summary>
/// <see cref="ISerialTransport"/> straight on the Win32 COM port API: plain blocking reads and writes
/// on the device's own worker thread. Unlike System.IO.Ports it starts no helper thread per port and
/// allocates nothing per write. Settings: 115200 8N1, DTR on, no handshake, 6 s write timeout.
/// </summary>
internal sealed class Win32SerialTransport(string portName) : ISerialTransport
{
    /// <summary>Longer than the firmware's blocking fade when it wakes or sleeps (see MatrixDevice.WakeTimeout).</summary>
    private const int WriteTimeoutMs = 6000;

    private SafeFileHandle? _handle;
    private uint _readTimeoutMs = uint.MaxValue;

    public string PortName { get; } = portName;

    public bool IsOpen => _handle is { IsInvalid: false, IsClosed: false };

    public void Open()
    {
        Close();
        var handle = NativeMethods.CreateFileW(
            @"\\.\" + PortName,
            NativeMethods.GENERIC_READ | NativeMethods.GENERIC_WRITE,
            0, // exclusive, like every serial port
            IntPtr.Zero,
            NativeMethods.OPEN_EXISTING,
            0,
            IntPtr.Zero);

        if (handle.IsInvalid)
        {
            var error = Marshal.GetLastPInvokeError();
            handle.Dispose();
            throw error switch
            {
                NativeMethods.ERROR_ACCESS_DENIED => new UnauthorizedAccessException($"{PortName} is in use by another application."),
                _ => new IOException($"Could not open {PortName} (Win32 error {error})."),
            };
        }

        try
        {
            var dcb = new NativeMethods.DCB { DCBlength = (uint)Marshal.SizeOf<NativeMethods.DCB>() };
            Check(NativeMethods.GetCommState(handle, ref dcb), "GetCommState");
            dcb.BaudRate = Protocol.BaudRate;
            dcb.Flags = NativeMethods.DCB_BINARY | NativeMethods.DCB_DTR_CONTROL_ENABLE; // no parity, no flow control, RTS off
            dcb.ByteSize = 8;
            dcb.Parity = 0; // NOPARITY
            dcb.StopBits = 0; // ONESTOPBIT
            Check(NativeMethods.SetCommState(handle, ref dcb), "SetCommState");
            NativeMethods.PurgeComm(handle, NativeMethods.PURGE_RXCLEAR | NativeMethods.PURGE_TXCLEAR);
            _handle = handle;
            _readTimeoutMs = uint.MaxValue;
            SetReadTimeout(250);
        }
        catch
        {
            _handle = null;
            handle.Dispose();
            throw;
        }
    }

    public void Close()
    {
        var handle = _handle;
        _handle = null;
        handle?.Dispose();
    }

    public void Write(ReadOnlySpan<byte> data)
    {
        var handle = OpenHandle();
        if (!NativeMethods.WriteFile(handle, ref MemoryMarshal.GetReference(data), (uint)data.Length, out var written, IntPtr.Zero))
        {
            throw new IOException($"Write to {PortName} failed (Win32 error {Marshal.GetLastPInvokeError()}).");
        }

        if (written < data.Length)
        {
            throw new TimeoutException($"Write to {PortName} timed out.");
        }
    }

    public int Read(byte[] buffer, TimeSpan timeout)
    {
        var handle = OpenHandle();
        SetReadTimeout((uint)Math.Clamp(timeout.TotalMilliseconds, 1, int.MaxValue));
        if (!NativeMethods.ReadFile(handle, ref MemoryMarshal.GetArrayDataReference(buffer), (uint)buffer.Length, out var read, IntPtr.Zero))
        {
            throw new IOException($"Read from {PortName} failed (Win32 error {Marshal.GetLastPInvokeError()}).");
        }

        return (int)read;
    }

    public void DiscardInput()
    {
        if (IsOpen)
        {
            NativeMethods.PurgeComm(_handle!, NativeMethods.PURGE_RXCLEAR);
        }
    }

    public void Dispose() => Close();

    private SafeFileHandle OpenHandle() =>
        _handle is { IsInvalid: false, IsClosed: false } h ? h : throw new InvalidOperationException("Port is not open.");

    /// <summary>Reads return as soon as any byte arrives, or after <paramref name="ms"/> with nothing; writes give up after 6 s.</summary>
    private void SetReadTimeout(uint ms)
    {
        if (ms == _readTimeoutMs)
        {
            return;
        }

        var timeouts = new NativeMethods.COMMTIMEOUTS
        {
            ReadIntervalTimeout = uint.MaxValue,
            ReadTotalTimeoutMultiplier = uint.MaxValue,
            ReadTotalTimeoutConstant = ms,
            WriteTotalTimeoutMultiplier = 0,
            WriteTotalTimeoutConstant = WriteTimeoutMs,
        };
        Check(NativeMethods.SetCommTimeouts(OpenHandle(), ref timeouts), "SetCommTimeouts");
        _readTimeoutMs = ms;
    }

    private void Check(bool ok, string call)
    {
        if (!ok)
        {
            var error = Marshal.GetLastPInvokeError();
            throw new IOException($"{call} on {PortName} failed (Win32 error {error}).");
        }
    }
}
