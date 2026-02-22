using System.Collections.Concurrent;

namespace Emulator.Bios;

/// <summary>
/// Thread-safe queue for terminal input bytes.
/// The CPU thread blocks on BlockingRead() until a keystroke arrives
/// from the SignalR hub thread via Enqueue().
/// </summary>
public sealed class TerminalInputQueue
{
    private readonly BlockingCollection<byte> _queue = new(boundedCapacity: 1024);

    /// <summary>Called from the SignalR hub thread when a key is pressed.</summary>
    public void Enqueue(IEnumerable<byte> bytes)
    {
        foreach (var b in bytes)
        {
            _queue.TryAdd(b);
        }
    }

    public void Enqueue(byte b) => _queue.TryAdd(b);

    /// <summary>
    /// Called from the CPU thread (CONIN BIOS function).
    /// Blocks until a byte is available or cancellation is requested.
    /// </summary>
    public byte BlockingRead(CancellationToken ct = default)
    {
        try
        {
            return _queue.Take(ct);
        }
        catch (OperationCanceledException)
        {
            return 0x1B; // ESC on cancel (causes most CP/M programs to abort cleanly)
        }
    }

    /// <summary>Called from the CPU thread (CONST BIOS function).</summary>
    public bool HasData() => _queue.Count > 0;

    /// <summary>Drain the queue (called on reset).</summary>
    public void Clear()
    {
        while (_queue.TryTake(out _)) { }
    }
}
