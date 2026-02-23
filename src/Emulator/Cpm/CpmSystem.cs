using Emulator.Bios;
using Emulator.CPU;
using Emulator.Disk;
using Emulator.IO;
using Emulator.Memory;

namespace Emulator.Cpm;

/// <summary>
/// Top-level orchestrator for the CP/M 2.2 emulated machine.
/// Owns memory, CPU, BIOS, BDOS, CCP, and disk system.
/// All software (CCP, BDOS, editor, assembler, BASIC) is implemented in .NET —
/// no external CP/M binaries required.
/// </summary>
public sealed class CpmSystem
{
    public MemoryBus Memory { get; }
    public I8080 Cpu { get; }
    public DiskSystem Disks { get; }
    public TerminalInputQueue Input { get; }

    private readonly BiosHandler _bios;
    private readonly BdosHandler _bdos;
    private readonly IoPort _io;

    // Per-drive CP/M file system (built from mounted disk images)
    private readonly CpmDisk?[] _cpmDrives = new CpmDisk?[4];

    public event Action<int, bool>? DiskActivity;

    public CpmSystem(TerminalInputQueue input, Action<string> consoleOutput)
    {
        Input = input;

        Memory = new MemoryBus();
        Disks  = new DiskSystem();
        _io    = new IoPort();

        _bios = new BiosHandler(input, consoleOutput, Disks, Memory);
        _bios.DiskActivity += (drive, isWrite) => DiskActivity?.Invoke(drive, isWrite);

        _bdos = new BdosHandler(input, consoleOutput, Disks, Memory);
        _bios.SetBdos(_bdos);
        _bios.SetDrives(_cpmDrives);

        _io.SetBiosHandler(_bios);
        _io.SetBdosHandler(_bdos);

        Cpu = new I8080(Memory, _io);
    }

    /// <summary>
    /// Initialize the machine: write BIOS stubs, BDOS stub, DPH/DPB, set PC.
    /// No external binary required — the CCP and BDOS are .NET implementations.
    /// </summary>
    public void Initialize()
    {
        // Write BIOS jump table at 0xFA00 (17 entries, 3 bytes each)
        for (int fn = 0; fn < 17; fn++)
        {
            int jmpAddr  = MemoryMap.BIOS_BASE + fn * 3;
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
        Cpu.SP = (ushort)(MemoryMap.BIOS_BASE - 2);
    }

    private void InitZeroPage()
    {
        // 0x0000: JMP WBOOT (BIOS entry 1)
        ushort wboot = (ushort)(MemoryMap.BIOS_BASE + 3);
        Memory.Write(0x0000, 0xC3);
        Memory.Write(0x0001, (byte)wboot);
        Memory.Write(0x0002, (byte)(wboot >> 8));

        // 0x0003: IOBYTE
        Memory.Write(0x0003, 0x00);

        // 0x0004: Current drive (A)
        Memory.Write(0x0004, 0x00);

        // 0x0005: BDOS stub — OUT BDOS_PORT, A + RET
        // This traps all CALL 5 calls from 8080 programs to the .NET BDOS handler
        Memory.Write(0x0005, 0xD3);                         // OUT
        Memory.Write(0x0006, MemoryMap.BDOS_PORT);          // port 17
        Memory.Write(0x0007, 0xC9);                         // RET
    }

    /// <summary>Mount a disk image on the given drive (0=A, 1=B, ...).</summary>
    public void MountDisk(int drive, DiskImage image)
    {
        Disks.MountDisk(drive, image);

        // Build CP/M file system for this drive
        var cpmDisk = new CpmDisk(image);
        _cpmDrives[drive] = cpmDisk;
        _bdos.MountDrive(drive, cpmDisk);
    }

    public void UnmountDisk(int drive)
    {
        // Flush any dirty files before unmounting
        _cpmDrives[drive]?.Flush();
        _cpmDrives[drive] = null;
        _bdos.UnmountDrive(drive);
        Disks.EjectDisk(drive);
    }

    /// <summary>Set the cancellation token used by BIOS blocking calls (CONIN, CCP).</summary>
    public void SetRunToken(CancellationToken ct) => _bios.RunToken = ct;

    /// <summary>Get the CpmDisk file system for the specified drive (0=A).</summary>
    public CpmDisk? GetCpmDisk(int drive) =>
        drive >= 0 && drive < 4 ? _cpmDrives[drive] : null;

    /// <summary>Flush any dirty files on the specified drive to the disk image.</summary>
    public void FlushDrive(int drive) => _cpmDrives[drive]?.Flush();

    /// <summary>Get CPU state snapshot for SignalR streaming.</summary>
    public CpuStateDto GetCpuState(bool isRunning, int speedHz)
    {
        string disasm;
        try { disasm = Cpu.Disassemble(Cpu.PC, out _); }
        catch { disasm = "???"; }

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
