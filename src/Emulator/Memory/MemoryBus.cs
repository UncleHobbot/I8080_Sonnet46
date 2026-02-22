namespace Emulator.Memory;

public sealed class MemoryBus
{
    private readonly byte[] _ram = new byte[MemoryMap.RAM_SIZE];

    public byte Read(ushort address) => _ram[address];

    public void Write(ushort address, byte value) => _ram[address] = value;

    public ushort ReadWord(ushort address)
    {
        byte lo = _ram[address];
        byte hi = _ram[(ushort)(address + 1)];
        return (ushort)((hi << 8) | lo);
    }

    public void WriteWord(ushort address, ushort value)
    {
        _ram[address] = (byte)value;
        _ram[(ushort)(address + 1)] = (byte)(value >> 8);
    }

    public void ReadBlock(ushort address, Span<byte> dest)
    {
        _ram.AsSpan(address, dest.Length).CopyTo(dest);
    }

    public void WriteBlock(ushort address, ReadOnlySpan<byte> src)
    {
        src.CopyTo(_ram.AsSpan(address, src.Length));
    }

    /// <summary>Load raw bytes into memory at the given address.</summary>
    public void Load(int address, byte[] data) =>
        data.CopyTo(_ram, address);

    /// <summary>Load a span of bytes into memory at the given address.</summary>
    public void Load(int address, ReadOnlySpan<byte> data) =>
        data.CopyTo(_ram.AsSpan(address));

    /// <summary>Return a copy of a memory region (for debug/UI).</summary>
    public byte[] Dump(int address, int length)
    {
        var result = new byte[length];
        Array.Copy(_ram, address, result, 0, length);
        return result;
    }

    /// <summary>Write a single byte at an integer address (for init code convenience).</summary>
    public void Write(int address, byte value) => _ram[address] = value;
}
