using System.Runtime.CompilerServices;
using Emulator.IO;
using Emulator.Memory;

namespace Emulator.CPU;

/// <summary>
/// Intel 8080A CPU emulator.
/// The opcode implementations live in the partial class I8080.Instructions.cs.
/// </summary>
public sealed partial class I8080
{
    // ── Registers ────────────────────────────────────────────────────────────
    public byte A;
    public byte B, C;
    public byte D, E;
    public byte H, L;
    public ushort SP;
    public ushort PC;

    // Flags
    public bool FlagS;    // Sign
    public bool FlagZ;    // Zero
    public bool FlagAC;   // Auxiliary Carry (half-carry)
    public bool FlagP;    // Parity
    public bool FlagCY;   // Carry

    // Interrupt state
    public bool IFF;      // Interrupt flip-flop (EI/DI)
    public bool Halted;

    // Cycle counter
    public long TotalCycles { get; private set; }

    // ── Register pair convenience properties ─────────────────────────────────
    public ushort BC
    {
        get => (ushort)((B << 8) | C);
        set { B = (byte)(value >> 8); C = (byte)value; }
    }

    public ushort DE
    {
        get => (ushort)((D << 8) | E);
        set { D = (byte)(value >> 8); E = (byte)value; }
    }

    public ushort HL
    {
        get => (ushort)((H << 8) | L);
        set { H = (byte)(value >> 8); L = (byte)value; }
    }

    // ── Hardware buses ────────────────────────────────────────────────────────
    public MemoryBus Memory { get; }
    private readonly IoPort _io;

    public I8080(MemoryBus memory, IoPort io)
    {
        Memory = memory;
        _io = io;
    }

    // ── Reset ─────────────────────────────────────────────────────────────────
    public void Reset()
    {
        A = B = C = D = E = H = L = 0;
        SP = PC = 0;
        FlagS = FlagZ = FlagAC = FlagP = FlagCY = false;
        IFF = false;
        Halted = false;
    }

    // ── Main execution step ───────────────────────────────────────────────────
    /// <summary>Execute one instruction, return number of cycles consumed.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int ExecuteOne()
    {
        if (Halted) return 4;
        byte opcode = Fetch();
        int cycles = Dispatch(opcode);
        TotalCycles += cycles;
        return cycles;
    }

    // ── Fetch helpers ─────────────────────────────────────────────────────────
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private byte Fetch() => Memory.Read(PC++);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ushort FetchWord()
    {
        byte lo = Fetch();
        byte hi = Fetch();
        return (ushort)((hi << 8) | lo);
    }

    // ── Stack helpers ─────────────────────────────────────────────────────────
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void Push(ushort value)
    {
        Memory.Write(--SP, (byte)(value >> 8));
        Memory.Write(--SP, (byte)value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ushort Pop()
    {
        byte lo = Memory.Read(SP++);
        byte hi = Memory.Read(SP++);
        return (ushort)((hi << 8) | lo);
    }

    // ── Flag helpers ──────────────────────────────────────────────────────────
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void SetSZP(byte result)
    {
        FlagS = (result & 0x80) != 0;
        FlagZ = result == 0;
        FlagP = Flags.Parity(result);
    }

    // Compute auxiliary carry for addition: set if carry from bit 3 to bit 4
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool AuxCarryAdd(byte a, byte b) => ((a & 0x0F) + (b & 0x0F)) > 0x0F;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool AuxCarryAddC(byte a, byte b, bool cy) =>
        ((a & 0x0F) + (b & 0x0F) + (cy ? 1 : 0)) > 0x0F;

    // Compute auxiliary carry for subtraction: borrow from bit 4
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool AuxCarrySub(byte a, byte b) => (a & 0x0F) < (b & 0x0F);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool AuxCarrySubC(byte a, byte b, bool cy) =>
        (a & 0x0F) < ((b & 0x0F) + (cy ? 1 : 0));

    // ── ALU helpers ───────────────────────────────────────────────────────────
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private byte Add(byte a, byte b)
    {
        int result = a + b;
        FlagCY = result > 0xFF;
        FlagAC = AuxCarryAdd(a, b);
        byte r = (byte)result;
        SetSZP(r);
        return r;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private byte Adc(byte a, byte b)
    {
        bool carry = FlagCY;
        int result = a + b + (carry ? 1 : 0);
        FlagCY = result > 0xFF;
        FlagAC = AuxCarryAddC(a, b, carry);
        byte r = (byte)result;
        SetSZP(r);
        return r;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private byte Sub(byte a, byte b)
    {
        int result = a - b;
        FlagCY = result < 0;
        FlagAC = AuxCarrySub(a, b);
        byte r = (byte)result;
        SetSZP(r);
        return r;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private byte Sbb(byte a, byte b)
    {
        bool carry = FlagCY;
        int result = a - b - (carry ? 1 : 0);
        FlagCY = result < 0;
        FlagAC = AuxCarrySubC(a, b, carry);
        byte r = (byte)result;
        SetSZP(r);
        return r;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void Ana(byte b)
    {
        FlagAC = ((A | b) & 0x08) != 0; // 8080 AC behavior for ANA
        A &= b;
        FlagCY = false;
        SetSZP(A);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void Xra(byte b)
    {
        A ^= b;
        FlagCY = false;
        FlagAC = false;
        SetSZP(A);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void Ora(byte b)
    {
        A |= b;
        FlagCY = false;
        FlagAC = false;
        SetSZP(A);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void Cmp(byte b)
    {
        int result = A - b;
        FlagCY = result < 0;
        FlagAC = AuxCarrySub(A, b);
        SetSZP((byte)result);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private byte Inr(byte b)
    {
        FlagAC = (b & 0x0F) == 0x0F;
        b++;
        SetSZP(b);
        // CY not affected by INR
        return b;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private byte Dcr(byte b)
    {
        FlagAC = (b & 0x0F) != 0x00;
        b--;
        SetSZP(b);
        // CY not affected by DCR
        return b;
    }

    // Double-precision add: HL += rp; only affects CY
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void Dad(ushort rp)
    {
        uint result = (uint)HL + rp;
        FlagCY = result > 0xFFFF;
        HL = (ushort)result;
    }
}
