using Emulator.Cpm;
using Emulator.CPU;
using Emulator.Disk;
using Emulator.Memory;

namespace Emulator.Bios;

/// <summary>
/// CP/M 2.2 BIOS implemented in .NET.
/// The 17 BIOS functions are called via OUT port stubs (ports 0–16) written into memory.
/// BOOT and WBOOT delegate to the .NET CCP command processor.
/// </summary>
public sealed class BiosHandler
{
    private readonly TerminalInputQueue _input;
    private readonly Action<string>    _output;
    private readonly DiskSystem        _disks;
    private readonly MemoryBus         _mem;

    // Injected after construction to break circular dependency
    private BdosHandler? _bdos;
    private CpmDisk?[]?  _drives;
    private CcpHandler?  _ccp;

    /// <summary>
    /// Cancellation token set by the EmulatorService CPU loop.
    /// Used to propagate server shutdown into blocking CCP/CONIN calls.
    /// </summary>
    public CancellationToken RunToken { get; set; } = CancellationToken.None;

    public event Action<int, bool>? DiskActivity; // drive, isWrite

    public BiosHandler(
        TerminalInputQueue input,
        Action<string>     output,
        DiskSystem         disks,
        MemoryBus          mem)
    {
        _input  = input;
        _output = output;
        _disks  = disks;
        _mem    = mem;
    }

    public void SetBdos(BdosHandler bdos)    => _bdos = bdos;
    public void SetDrives(CpmDisk?[] drives) => _drives = drives;

    // ── Main dispatch ──────────────────────────────────────────────────────────

    public void Execute(BiosFunction fn, I8080 cpu)
    {
        switch (fn)
        {
            case BiosFunction.BOOT:   Boot(cpu);   break;
            case BiosFunction.WBOOT:  WarmBoot(cpu); break;
            case BiosFunction.CONST:  cpu.A = _input.HasData() ? (byte)0xFF : (byte)0x00; break;
            case BiosFunction.CONIN:  cpu.A = _input.BlockingRead(RunToken); break;
            case BiosFunction.CONOUT:
                char ch = (char)cpu.C;
                _output(ch == '\r' ? "\r\n" : ch.ToString());
                break;
            case BiosFunction.LIST:   break; // printer — ignore
            case BiosFunction.PUNCH:  break; // punch  — ignore
            case BiosFunction.READER: cpu.A = 0x1A; break; // EOF
            case BiosFunction.LISTST: cpu.A = 0xFF;  break; // always ready
            case BiosFunction.HOME:   _disks.SetTrack(0); break;
            case BiosFunction.SELDSK: cpu.HL = _disks.SelectDisk(cpu.C); break;
            case BiosFunction.SETTRK: _disks.SetTrack(cpu.BC); break;
            case BiosFunction.SETSEC: _disks.SetSector(cpu.BC); break;
            case BiosFunction.SETDMA: _disks.SetDma(cpu.BC); break;
            case BiosFunction.READ:
                DiskActivity?.Invoke(_disks.SelectedDrive, false);
                cpu.A = _disks.ReadSector(_mem);
                break;
            case BiosFunction.WRITE:
                DiskActivity?.Invoke(_disks.SelectedDrive, true);
                cpu.A = _disks.WriteSector(_mem);
                break;
            case BiosFunction.SECTRAN:
                cpu.HL = cpu.DE == 0 ? cpu.BC : _mem.ReadWord((ushort)(cpu.DE + cpu.BC - 1));
                break;
        }
    }

    // ── Boot handlers ─────────────────────────────────────────────────────────

    private void Boot(I8080 cpu)
    {
        // Cold boot: initialize zero page then start the .NET CCP
        InitZeroPage(cpu);
        RunCcp(cpu);
    }

    private void WarmBoot(I8080 cpu)
    {
        // Warm boot: reinitialize zero page and restart the .NET CCP
        _input.Clear();
        InitZeroPage(cpu);
        RunCcp(cpu);
    }

    private void RunCcp(I8080 cpu)
    {
        // Lazily create CCP on first use (after BDOS and drives are wired up)
        if (_ccp == null && _bdos != null && _drives != null)
        {
            _ccp = new CcpHandler(_input, _output, _bdos, _disks, _mem, _drives);
        }

        if (_ccp == null)
        {
            // BDOS/drives not yet available — just halt
            cpu.Halted = true;
            return;
        }

        // Run the CCP on the CPU thread. It blocks reading from the terminal
        // and returns only when it has set cpu.PC to a .COM program in TPA.
        _ccp.Run(cpu, RunToken);

        // When Run() returns, cpu.PC = 0x0100 (TPA start of loaded .COM).
        // The CPU executor in EmulatorService.CpuLoop() will resume from there.
    }

    private void InitZeroPage(I8080 cpu)
    {
        // 0x0000: JMP WBOOT (BIOS entry 1)
        ushort wboot = (ushort)(MemoryMap.BIOS_BASE + 3);
        _mem.Write(0x0000, 0xC3);
        _mem.Write(0x0001, (byte)wboot);
        _mem.Write(0x0002, (byte)(wboot >> 8));

        // 0x0003: IOBYTE
        _mem.Write(0x0003, 0x00);

        // 0x0004: Current drive
        _mem.Write(0x0004, (byte)(_ccp?.CurrentDrive ?? 0));

        // 0x0005: BDOS stub — OUT BDOS_PORT, A + RET
        _mem.Write(0x0005, 0xD3);
        _mem.Write(0x0006, MemoryMap.BDOS_PORT);
        _mem.Write(0x0007, 0xC9);

        cpu.SP = (ushort)(MemoryMap.BIOS_BASE - 2);
    }
}
