using Emulator.Bios;
using Emulator.CPU;
using Emulator.IO;
using Emulator.Memory;

namespace Emulator.Tests.CPU;

/// <summary>
/// Unit tests for Intel 8080 opcode implementations.
/// Tests cover flags, arithmetic, data transfer, and branch instructions.
/// </summary>
public class OpcodeTests
{
    private static (I8080 cpu, MemoryBus mem) MakeCpu(params byte[] program)
    {
        var mem = new MemoryBus();
        var io = new IoPort();
        var cpu = new I8080(mem, io);
        cpu.Reset();
        cpu.PC = 0x0100;
        cpu.SP = 0xFF00;
        mem.Load(0x0100, program);
        return (cpu, mem);
    }

    private static void Run(I8080 cpu, int steps = 1)
    {
        for (int i = 0; i < steps; i++) cpu.ExecuteOne();
    }

    // ── NOP ──────────────────────────────────────────────────────────────────
    [Fact]
    public void NOP_ConsumesNoCycles_ChangesNothing()
    {
        var (cpu, _) = MakeCpu(0x00);
        var pc = cpu.PC;
        var cycles = cpu.ExecuteOne();
        Assert.Equal(4, cycles);
        Assert.Equal((ushort)(pc + 1), cpu.PC);
    }

    // ── MOV ──────────────────────────────────────────────────────────────────
    [Fact]
    public void MOV_B_C_CopiesRegister()
    {
        var (cpu, _) = MakeCpu(0x41); // MOV B,C
        cpu.C = 0x42;
        Run(cpu);
        Assert.Equal(0x42, cpu.B);
    }

    // ── MVI ──────────────────────────────────────────────────────────────────
    [Fact]
    public void MVI_A_LoadsImmediate()
    {
        var (cpu, _) = MakeCpu(0x3E, 0x55); // MVI A,55h
        Run(cpu);
        Assert.Equal(0x55, cpu.A);
    }

    // ── ADD ──────────────────────────────────────────────────────────────────
    [Fact]
    public void ADD_B_BasicAddition()
    {
        var (cpu, _) = MakeCpu(0x80); // ADD B
        cpu.A = 0x10;
        cpu.B = 0x20;
        Run(cpu);
        Assert.Equal(0x30, cpu.A);
        Assert.False(cpu.FlagZ);
        Assert.False(cpu.FlagCY);
    }

    [Fact]
    public void ADD_B_SetsCarryOnOverflow()
    {
        var (cpu, _) = MakeCpu(0x80); // ADD B
        cpu.A = 0xFF;
        cpu.B = 0x01;
        Run(cpu);
        Assert.Equal(0x00, cpu.A);
        Assert.True(cpu.FlagZ);
        Assert.True(cpu.FlagCY);
    }

    [Fact]
    public void ADD_SetsSignFlag()
    {
        var (cpu, _) = MakeCpu(0x80); // ADD B
        cpu.A = 0x7F;
        cpu.B = 0x01;
        Run(cpu);
        Assert.Equal(0x80, cpu.A);
        Assert.True(cpu.FlagS); // result has bit 7 set
    }

    [Fact]
    public void ADD_SetsAuxiliaryCarry()
    {
        var (cpu, _) = MakeCpu(0x80); // ADD B
        cpu.A = 0x0F;
        cpu.B = 0x01;
        Run(cpu);
        Assert.Equal(0x10, cpu.A);
        Assert.True(cpu.FlagAC); // carry from bit 3 to bit 4
    }

    // ── ADC ──────────────────────────────────────────────────────────────────
    [Fact]
    public void ADC_B_AddsCarry()
    {
        var (cpu, _) = MakeCpu(0x88); // ADC B
        cpu.A = 0x10;
        cpu.B = 0x10;
        cpu.FlagCY = true;
        Run(cpu);
        Assert.Equal(0x21, cpu.A); // 0x10 + 0x10 + 1
    }

    // ── SUB ──────────────────────────────────────────────────────────────────
    [Fact]
    public void SUB_B_BasicSubtraction()
    {
        var (cpu, _) = MakeCpu(0x90); // SUB B
        cpu.A = 0x30;
        cpu.B = 0x10;
        Run(cpu);
        Assert.Equal(0x20, cpu.A);
        Assert.False(cpu.FlagCY);
        Assert.False(cpu.FlagZ);
    }

    [Fact]
    public void SUB_SetsCarryOnBorrow()
    {
        var (cpu, _) = MakeCpu(0x90); // SUB B
        cpu.A = 0x10;
        cpu.B = 0x20;
        Run(cpu);
        Assert.Equal(0xF0, cpu.A);
        Assert.True(cpu.FlagCY); // borrow sets CY
    }

    [Fact]
    public void SUB_A_ZerosRegister()
    {
        var (cpu, _) = MakeCpu(0x97); // SUB A
        cpu.A = 0x42;
        Run(cpu);
        Assert.Equal(0x00, cpu.A);
        Assert.True(cpu.FlagZ);
        Assert.False(cpu.FlagCY);
    }

    // ── INR / DCR ─────────────────────────────────────────────────────────────
    [Fact]
    public void INR_A_DoesNotAffectCarry()
    {
        var (cpu, _) = MakeCpu(0x3C); // INR A
        cpu.A = 0xFF;
        cpu.FlagCY = true;
        Run(cpu);
        Assert.Equal(0x00, cpu.A);
        Assert.True(cpu.FlagZ);
        Assert.True(cpu.FlagCY); // CY unchanged by INR
    }

    [Fact]
    public void DCR_B_DoesNotAffectCarry()
    {
        var (cpu, _) = MakeCpu(0x05); // DCR B
        cpu.B = 0x00;
        cpu.FlagCY = false;
        Run(cpu);
        Assert.Equal(0xFF, cpu.B);
        Assert.False(cpu.FlagCY); // CY unchanged by DCR
        Assert.True(cpu.FlagS);
    }

    // ── DAD ──────────────────────────────────────────────────────────────────
    [Fact]
    public void DAD_B_OnlySetsCarry()
    {
        var (cpu, _) = MakeCpu(0x09); // DAD B
        cpu.HL = 0xFFFF;
        cpu.BC = 0x0001;
        cpu.FlagZ = true;  // should not be cleared by DAD
        cpu.FlagS = true;
        Run(cpu);
        Assert.Equal(0x0000, cpu.HL);
        Assert.True(cpu.FlagCY);
        Assert.True(cpu.FlagZ);  // Z unchanged
        Assert.True(cpu.FlagS);  // S unchanged
    }

    // ── ANA / ORA / XRA ───────────────────────────────────────────────────────
    [Fact]
    public void ANA_B_ClearsCY()
    {
        var (cpu, _) = MakeCpu(0xA0); // ANA B
        cpu.A = 0xFF;
        cpu.B = 0x0F;
        cpu.FlagCY = true;
        Run(cpu);
        Assert.Equal(0x0F, cpu.A);
        Assert.False(cpu.FlagCY);
    }

    [Fact]
    public void XRA_A_ZerosAccumulator()
    {
        var (cpu, _) = MakeCpu(0xAF); // XRA A
        cpu.A = 0x42;
        Run(cpu);
        Assert.Equal(0x00, cpu.A);
        Assert.True(cpu.FlagZ);
        Assert.False(cpu.FlagCY);
    }

    // ── CMP ──────────────────────────────────────────────────────────────────
    [Fact]
    public void CMP_B_EqualSetsZero()
    {
        var (cpu, _) = MakeCpu(0xB8); // CMP B
        cpu.A = 0x42;
        cpu.B = 0x42;
        Run(cpu);
        Assert.True(cpu.FlagZ);
        Assert.False(cpu.FlagCY);
        Assert.Equal(0x42, cpu.A); // A unchanged
    }

    [Fact]
    public void CMP_B_LessThanSetsCarry()
    {
        var (cpu, _) = MakeCpu(0xB8); // CMP B
        cpu.A = 0x10;
        cpu.B = 0x20;
        Run(cpu);
        Assert.False(cpu.FlagZ);
        Assert.True(cpu.FlagCY);
    }

    // ── JMP ──────────────────────────────────────────────────────────────────
    [Fact]
    public void JMP_UnconditionalJump()
    {
        var (cpu, mem) = MakeCpu(0xC3, 0x50, 0x02); // JMP 0x0250
        Run(cpu);
        Assert.Equal(0x0250, cpu.PC);
    }

    [Fact]
    public void JZ_JumpsWhenZeroSet()
    {
        var (cpu, _) = MakeCpu(0xCA, 0x50, 0x02); // JZ 0x0250
        cpu.FlagZ = true;
        Run(cpu);
        Assert.Equal(0x0250, cpu.PC);
    }

    [Fact]
    public void JZ_DoesNotJumpWhenZeroClear()
    {
        var (cpu, _) = MakeCpu(0xCA, 0x50, 0x02); // JZ 0x0250
        cpu.FlagZ = false;
        Run(cpu);
        Assert.Equal((ushort)0x0103, cpu.PC); // fell through (0x0100 + 3)
    }

    // ── CALL / RET ────────────────────────────────────────────────────────────
    [Fact]
    public void CALL_PushesReturnAddr_And_Jumps()
    {
        var (cpu, mem) = MakeCpu(0xCD, 0x00, 0x02); // CALL 0x0200
        Run(cpu);
        Assert.Equal(0x0200, cpu.PC);
        // Return address (0x0103) should be on stack
        ushort retAddr = mem.ReadWord(cpu.SP);
        Assert.Equal((ushort)0x0103, retAddr);
    }

    [Fact]
    public void RET_PopsReturnAddress()
    {
        var (cpu, mem) = MakeCpu(0xCD, 0x00, 0x02); // CALL 0x0200
        mem.Write(0x0200, 0xC9); // RET at 0x0200
        Run(cpu, 2);
        Assert.Equal((ushort)0x0103, cpu.PC);
    }

    // ── PUSH / POP PSW ────────────────────────────────────────────────────────
    [Fact]
    public void PUSH_PSW_POP_PSW_RoundTrips()
    {
        var (cpu, _) = MakeCpu(0xF5, 0xF1); // PUSH PSW, POP PSW
        cpu.A = 0xAB;
        cpu.FlagS = true;
        cpu.FlagZ = false;
        cpu.FlagAC = true;
        cpu.FlagP = false;
        cpu.FlagCY = true;

        // Save original flags
        bool s = cpu.FlagS, z = cpu.FlagZ, ac = cpu.FlagAC, p = cpu.FlagP, cy = cpu.FlagCY;
        byte a = cpu.A;

        Run(cpu); // PUSH PSW
        cpu.A = 0x00;
        cpu.FlagS = cpu.FlagZ = cpu.FlagAC = cpu.FlagP = cpu.FlagCY = false;

        Run(cpu); // POP PSW
        Assert.Equal(a, cpu.A);
        Assert.Equal(s, cpu.FlagS);
        Assert.Equal(z, cpu.FlagZ);
        Assert.Equal(ac, cpu.FlagAC);
        Assert.Equal(p, cpu.FlagP);
        Assert.Equal(cy, cpu.FlagCY);
    }

    // ── LXI / INX / DCX ───────────────────────────────────────────────────────
    [Fact]
    public void LXI_BC_LoadsRegisterPair()
    {
        var (cpu, _) = MakeCpu(0x01, 0x34, 0x12); // LXI B,0x1234
        Run(cpu);
        Assert.Equal(0x1234, cpu.BC);
        Assert.Equal(0x12, cpu.B);
        Assert.Equal(0x34, cpu.C);
    }

    [Fact]
    public void INX_H_WrapsAround()
    {
        var (cpu, _) = MakeCpu(0x23); // INX H
        cpu.HL = 0xFFFF;
        Run(cpu);
        Assert.Equal(0x0000, cpu.HL);
    }

    // ── RLC / RRC / RAL / RAR ─────────────────────────────────────────────────
    [Fact]
    public void RLC_RotatesLeftThroughBit7()
    {
        var (cpu, _) = MakeCpu(0x07); // RLC
        cpu.A = 0b10000001;
        Run(cpu);
        Assert.Equal(0b00000011, cpu.A);
        Assert.True(cpu.FlagCY); // bit 7 was 1
    }

    [Fact]
    public void RAL_RotatesLeftThroughCarry()
    {
        var (cpu, _) = MakeCpu(0x17); // RAL
        cpu.A = 0b00000001;
        cpu.FlagCY = true;
        Run(cpu);
        Assert.Equal(0b00000011, cpu.A);
        Assert.False(cpu.FlagCY); // old bit 7 was 0
    }

    // ── DAA ──────────────────────────────────────────────────────────────────
    [Fact]
    public void DAA_AdjustsAfterBCDAdd()
    {
        var (cpu, _) = MakeCpu(0x80, 0x27); // ADD B, DAA
        cpu.A = 0x09; // BCD 9
        cpu.B = 0x01; // BCD 1
        Run(cpu, 2);
        // 9 + 1 = 10 in BCD → 0x10 in binary
        Assert.Equal(0x10, cpu.A);
    }

    // ── XTHL / XCHG ──────────────────────────────────────────────────────────
    [Fact]
    public void XCHG_SwapsHLAndDE()
    {
        var (cpu, _) = MakeCpu(0xEB); // XCHG
        cpu.HL = 0x1234;
        cpu.DE = 0x5678;
        Run(cpu);
        Assert.Equal(0x5678, cpu.HL);
        Assert.Equal(0x1234, cpu.DE);
    }

    // ── STA / LDA ─────────────────────────────────────────────────────────────
    [Fact]
    public void STA_StoresAccumulatorAtAddr()
    {
        var (cpu, mem) = MakeCpu(0x32, 0x00, 0x03); // STA 0x0300
        cpu.A = 0xAB;
        Run(cpu);
        Assert.Equal(0xAB, mem.Read(0x0300));
    }

    [Fact]
    public void LDA_LoadsAccumulatorFromAddr()
    {
        var (cpu, mem) = MakeCpu(0x3A, 0x00, 0x03); // LDA 0x0300
        mem.Write(0x0300, 0xCD);
        Run(cpu);
        Assert.Equal(0xCD, cpu.A);
    }

    // ── HLT ──────────────────────────────────────────────────────────────────
    [Fact]
    public void HLT_SetsHaltedFlag()
    {
        var (cpu, _) = MakeCpu(0x76); // HLT
        Assert.False(cpu.Halted);
        Run(cpu);
        Assert.True(cpu.Halted);
    }

    // ── EI / DI ──────────────────────────────────────────────────────────────
    [Fact]
    public void EI_DI_TogglesInterruptFlag()
    {
        var (cpu, _) = MakeCpu(0xFB, 0xF3); // EI, DI
        Assert.False(cpu.IFF);
        Run(cpu); // EI
        Assert.True(cpu.IFF);
        Run(cpu); // DI
        Assert.False(cpu.IFF);
    }

    // ── Parity flag ───────────────────────────────────────────────────────────
    [Fact]
    public void ADD_SetsParityFlag_EvenBits()
    {
        var (cpu, _) = MakeCpu(0x80); // ADD B
        cpu.A = 0x00;
        cpu.B = 0x03; // 0x03 = 0b00000011, 2 bits set → even parity
        Run(cpu);
        Assert.Equal(0x03, cpu.A);
        Assert.True(cpu.FlagP); // even parity
    }

    [Fact]
    public void ADD_ClearsParityFlag_OddBits()
    {
        var (cpu, _) = MakeCpu(0x80); // ADD B
        cpu.A = 0x00;
        cpu.B = 0x01; // 1 bit set → odd parity
        Run(cpu);
        Assert.False(cpu.FlagP);
    }
}
