using System.IO.Ports;
using LedMatrix.Core;

namespace LedMatrix.App.Services;

/// <summary>
/// <see cref="ISerialTransport"/> on top of System.IO.Ports. Kept as a fallback for
/// <see cref="Win32SerialTransport"/>: set the environment variable LEDMATRIX_SERIAL=dotnet to use it.
/// </summary>
internal sealed class SerialPortTransport(string portName) : ISerialTransport
{
    private SerialPort? _port;

    public string PortName { get; } = portName;

    public bool IsOpen => _port?.IsOpen == true;

    public void Open()
    {
        Close();
        var port = new SerialPort(PortName, Protocol.BaudRate)
        {
            WriteTimeout = 500,
            ReadTimeout = 250,
            DtrEnable = true, // like pyserial / Web Serial, which the official tools use
            Handshake = Handshake.None,
        };

        try
        {
            port.Open(); // UnauthorizedAccessException when another app holds the port
        }
        catch
        {
            port.Dispose();
            throw;
        }

        _port = port;
    }

    public void Close()
    {
        var port = _port;
        _port = null;
        if (port is null)
        {
            return;
        }

        try
        {
            if (port.IsOpen)
            {
                port.Close();
            }
        }
        finally
        {
            port.Dispose();
        }
    }

    public void Write(ReadOnlySpan<byte> data)
    {
        var port = _port ?? throw new InvalidOperationException("Port is not open.");
        port.BaseStream.Write(data);
    }

    public int Read(byte[] buffer, TimeSpan timeout)
    {
        var port = _port ?? throw new InvalidOperationException("Port is not open.");
        port.ReadTimeout = Math.Max(1, (int)timeout.TotalMilliseconds);
        try
        {
            return port.Read(buffer, 0, buffer.Length);
        }
        catch (TimeoutException)
        {
            return 0;
        }
    }

    public void DiscardInput()
    {
        if (_port?.IsOpen == true)
        {
            _port.DiscardInBuffer();
        }
    }

    public void Dispose() => Close();
}
