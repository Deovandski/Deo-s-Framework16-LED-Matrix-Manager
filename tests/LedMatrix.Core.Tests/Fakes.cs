using LedMatrix.Core;

namespace LedMatrix.Core.Tests;

internal sealed class FakeTime : TimeProvider
{
    private DateTimeOffset _now = new(2026, 11, 4, 12, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
}

internal sealed class FakeTransport : ISerialTransport
{
    private readonly Queue<byte[]> _responses = new();

    public string PortName { get; init; } = "COM9";

    public bool IsOpen { get; private set; }

    public List<byte[]> Writes { get; } = [];

    public Exception? OpenException { get; set; }

    public Exception? WriteException { get; set; }

    public int OpenCount { get; private set; }

    public void EnqueueResponse(params byte[] response)
    {
        var r = new byte[Protocol.ResponseLength];
        response.CopyTo(r, 0);
        _responses.Enqueue(r);
    }

    public void Open()
    {
        OpenCount++;
        if (OpenException is not null)
        {
            throw OpenException;
        }

        IsOpen = true;
    }

    public void Close() => IsOpen = false;

    public void Write(byte[] data)
    {
        if (WriteException is not null)
        {
            throw WriteException;
        }

        Writes.Add(data);
    }

    public int Read(byte[] buffer, TimeSpan timeout)
    {
        if (_responses.Count == 0)
        {
            return 0;
        }

        var r = _responses.Dequeue();
        var n = Math.Min(r.Length, buffer.Length);
        Array.Copy(r, buffer, n);
        return n;
    }

    public void DiscardInput()
    {
    }

    public void Dispose() => IsOpen = false;

    /// <summary>Command ids of every write, for compact assertions.</summary>
    public List<Protocol.CommandId> Ids() => Writes.Select(w => (Protocol.CommandId)w[2]).ToList();
}
