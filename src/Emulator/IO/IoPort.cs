using Emulator.Bios;
using Emulator.CPU;

namespace Emulator.IO;

public sealed class IoPort
{
    private BiosHandler? _bios;

    public void SetBiosHandler(BiosHandler bios) => _bios = bios;

    /// <summary>CPU executed OUT port, value.</summary>
    public void Out(byte port, byte value, I8080 cpu)
    {
        if (port <= 16 && _bios != null)
        {
            _bios.Execute((BiosFunction)port, cpu);
        }
        // Additional ports can be wired here (e.g., debug output on port 0xFF)
    }

    /// <summary>CPU executed IN port.</summary>
    public byte In(byte port, I8080 cpu)
    {
        // Not typically used in this BIOS trap design
        return 0xFF;
    }
}
