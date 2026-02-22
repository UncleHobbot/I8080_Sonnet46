using System.Numerics;

namespace Emulator.CPU;

public static class Flags
{
    // CP/M PSW flag byte bit positions:
    // Bit 7: S (Sign)
    // Bit 6: Z (Zero)
    // Bit 5: 0 (always 0)
    // Bit 4: AC (Auxiliary Carry)
    // Bit 3: 0 (always 0)
    // Bit 2: P (Parity)
    // Bit 1: 1 (always 1)
    // Bit 0: CY (Carry)

    private static readonly bool[] _parity = BuildParityTable();

    private static bool[] BuildParityTable()
    {
        var t = new bool[256];
        for (int i = 0; i < 256; i++)
            t[i] = (BitOperations.PopCount((uint)i) & 1) == 0;
        return t;
    }

    public static bool Parity(byte b) => _parity[b];

    public static byte Pack(bool s, bool z, bool ac, bool p, bool cy) =>
        (byte)((s  ? 0x80 : 0) |
               (z  ? 0x40 : 0) |
               0x02             |   // bit 1 always 1
               (ac ? 0x10 : 0) |
               (p  ? 0x04 : 0) |
               (cy ? 0x01 : 0));

    public static (bool s, bool z, bool ac, bool p, bool cy) Unpack(byte psw) =>
        ((psw & 0x80) != 0,
         (psw & 0x40) != 0,
         (psw & 0x10) != 0,
         (psw & 0x04) != 0,
         (psw & 0x01) != 0);
}
