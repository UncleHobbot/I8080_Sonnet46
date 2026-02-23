using Emulator.Bios;
using Emulator.Cpm;
using Emulator.CPU;
using Emulator.Memory;

namespace Emulator.IO;

public sealed class IoPort
{
    private BiosHandler? _bios;
    private BdosHandler? _bdos;

    public void SetBiosHandler(BiosHandler bios) => _bios = bios;
    public void SetBdosHandler(BdosHandler bdos) => _bdos = bdos;

    /// <summary>CPU executed OUT port, value.</summary>
    public void Out(byte port, byte value, I8080 cpu)
    {
        if (port <= 16 && _bios != null)
        {
            _bios.Execute((BiosFunction)port, cpu);
        }
        else if (port == MemoryMap.BDOS_PORT && _bdos != null)
        {
            _bdos.Execute(cpu);
        }
    }

    /// <summary>CPU executed IN port.</summary>
    public byte In(byte port, I8080 cpu) => 0xFF;
}
