namespace LedMatrix.Core;

/// <summary>Abstraction over a serial port so the device logic can be unit tested.</summary>
public interface ISerialTransport : IDisposable
{
    string PortName { get; }

    bool IsOpen { get; }

    /// <summary>Opens the port. Throws <see cref="UnauthorizedAccessException"/> if another app holds it.</summary>
    void Open();

    void Close();

    /// <summary>Writes one complete command. The data may be a reused buffer: copy it if you keep it.</summary>
    void Write(ReadOnlySpan<byte> data);

    /// <summary>Reads up to <paramref name="buffer"/>.Length bytes, waiting at most <paramref name="timeout"/>. Returns bytes read (0 on timeout).</summary>
    int Read(byte[] buffer, TimeSpan timeout);

    void DiscardInput();
}
