using Emulator.Bios;
using Emulator.CPU;
using Emulator.Disk;
using Emulator.IO;
using Emulator.Memory;

namespace Emulator.Cpm;

/// <summary>
/// Top-level orchestrator for the CP/M 2.2 emulated machine.
/// Owns memory, CPU, BIOS, and disk system.
/// </summary>
public sealed class CpmSystem
{
    public MemoryBus Memory { get; }
    public I8080 Cpu { get; }
    public DiskSystem Disks { get; }
    public TerminalInputQueue Input { get; }

    private readonly BiosHandler _bios;
    private readonly IoPort _io;
    private readonly byte[] _cpmBinary;

    public event Action<int, bool>? DiskActivity;

    public CpmSystem(byte[] cpmBinary, TerminalInputQueue input, Action<string> consoleOutput)
    {
        _cpmBinary = cpmBinary;
        Input = input;

        Memory = new MemoryBus();
        Disks = new DiskSystem();
        _io = new IoPort();

        _bios = new BiosHandler(input, consoleOutput, Disks, cpmBinary, Memory);
        _bios.DiskActivity += (drive, isWrite) => DiskActivity?.Invoke(drive, isWrite);

        _io.SetBiosHandler(_bios);
        Cpu = new I8080(Memory, _io);
    }

    /// <summary>
    /// Initialize the machine: write BIOS stubs, DPH/DPB, load CP/M binary, set PC.
    /// </summary>
    public void Initialize()
    {
        // Load CP/M CCP+BDOS binary at 0xE400
        Memory.Load(MemoryMap.CCP_BASE, _cpmBinary);

        // Write BIOS jump table at 0xFA00
        // 17 entries, each is JMP (0xC3) to the corresponding stub
        for (int fn = 0; fn < 17; fn++)
        {
            int jmpAddr = MemoryMap.BIOS_BASE + fn * 3;
            int stubAddr = MemoryMap.BIOS_STUBS_BASE + fn * 3;
            Memory.Write(jmpAddr,     0xC3);
            Memory.Write(jmpAddr + 1, (byte)stubAddr);
            Memory.Write(jmpAddr + 2, (byte)(stubAddr >> 8));
        }

        // Write BIOS stubs: OUT fn, A (0xD3, fn) + RET (0xC9)
        for (int fn = 0; fn < 17; fn++)
        {
            int stubAddr = MemoryMap.BIOS_STUBS_BASE + fn * 3;
            Memory.Write(stubAddr,     0xD3); // OUT
            Memory.Write(stubAddr + 1, (byte)fn);
            Memory.Write(stubAddr + 2, 0xC9); // RET
        }

        // Initialize disk DPH/DPB structures in memory
        Disks.InitMemoryStructures(Memory);

        // Initialize zero page
        InitZeroPage();

        // CPU starts at BIOS BOOT (cold boot)
        Cpu.Reset();
        Cpu.PC = MemoryMap.BIOS_BASE; // JMP to BOOT stub → BIOS BOOT executes
        Cpu.SP = (ushort)(MemoryMap.BIOS_BASE - 2); // Stack just below BIOS
    }

    private void InitZeroPage()
    {
        // 0x0000: JMP WBOOT
        ushort wboot = (ushort)(MemoryMap.BIOS_BASE + 3);
        Memory.Write(0x0000, 0xC3);
        Memory.Write(0x0001, (byte)wboot);
        Memory.Write(0x0002, (byte)(wboot >> 8));

        // 0x0003: IOBYTE
        Memory.Write(0x0003, 0x00);

        // 0x0004: Current drive (A)
        Memory.Write(0x0004, 0x00);

        // 0x0005: JMP BDOS
        ushort bdos = (ushort)MemoryMap.BDOS_ENTRY;
        Memory.Write(0x0005, 0xC3);
        Memory.Write(0x0006, (byte)bdos);
        Memory.Write(0x0007, (byte)(bdos >> 8));
    }

    /// <summary>Mount a disk image on the given drive (0=A, 1=B, ...).</summary>
    public void MountDisk(int drive, DiskImage image) => Disks.MountDisk(drive, image);

    /// <summary>Get CPU state snapshot for SignalR streaming.</summary>
    public CpuStateDto GetCpuState(bool isRunning, int speedHz)
    {
        string disasm;
        try
        {
            disasm = Cpu.Disassemble(Cpu.PC, out _);
        }
        catch
        {
            disasm = "???";
        }

        return new CpuStateDto(
            Cpu.A, Cpu.B, Cpu.C, Cpu.D, Cpu.E, Cpu.H, Cpu.L,
            Cpu.SP, Cpu.PC,
            Cpu.FlagS, Cpu.FlagZ, Cpu.FlagAC, Cpu.FlagP, Cpu.FlagCY,
            Cpu.Halted, Cpu.IFF,
            Cpu.TotalCycles, isRunning, speedHz,
            disasm
        );
    }
}
