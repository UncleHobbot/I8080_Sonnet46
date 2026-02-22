using Emulator.CPU;
using Emulator.Disk;
using Emulator.Memory;

namespace Emulator.Bios;

public sealed class BiosHandler
{
    private readonly TerminalInputQueue _input;
    private readonly Action<string> _output;   // Push chars to SignalR
    private readonly DiskSystem _disks;
    private readonly byte[] _cpmBinary;         // cpm22.sys in memory for WBOOT reload
    private readonly MemoryBus _mem;

    public event Action<int, bool>? DiskActivity; // drive, isWrite

    public BiosHandler(
        TerminalInputQueue input,
        Action<string> output,
        DiskSystem disks,
        byte[] cpmBinary,
        MemoryBus mem)
    {
        _input = input;
        _output = output;
        _disks = disks;
        _cpmBinary = cpmBinary;
        _mem = mem;
    }

    public void Execute(BiosFunction fn, I8080 cpu)
    {
        switch (fn)
        {
            case BiosFunction.BOOT:
                Boot(cpu);
                break;

            case BiosFunction.WBOOT:
                WarmBoot(cpu);
                break;

            case BiosFunction.CONST:
                cpu.A = _input.HasData() ? (byte)0xFF : (byte)0x00;
                break;

            case BiosFunction.CONIN:
                cpu.A = _input.BlockingRead();
                break;

            case BiosFunction.CONOUT:
            {
                char ch = (char)cpu.C;
                // Translate bare CR to CR+LF for VT100 terminal
                _output(ch == '\r' ? "\r\n" : ch.ToString());
                break;
            }

            case BiosFunction.LIST:
                // Printer output - ignore for now
                break;

            case BiosFunction.PUNCH:
                // Punch output - ignore
                break;

            case BiosFunction.READER:
                cpu.A = 0x1A; // Ctrl-Z = EOF
                break;

            case BiosFunction.LISTST:
                cpu.A = 0xFF; // Always ready
                break;

            case BiosFunction.HOME:
                _disks.SetTrack(0);
                break;

            case BiosFunction.SELDSK:
                cpu.HL = _disks.SelectDisk(cpu.C);
                break;

            case BiosFunction.SETTRK:
                _disks.SetTrack(cpu.BC);
                break;

            case BiosFunction.SETSEC:
                _disks.SetSector(cpu.BC);
                break;

            case BiosFunction.SETDMA:
                _disks.SetDma(cpu.BC);
                break;

            case BiosFunction.READ:
                DiskActivity?.Invoke(_disks.SelectedDrive, false);
                cpu.A = _disks.ReadSector(_mem);
                break;

            case BiosFunction.WRITE:
                DiskActivity?.Invoke(_disks.SelectedDrive, true);
                cpu.A = _disks.WriteSector(_mem);
                break;

            case BiosFunction.SECTRAN:
                // BC = logical sector, DE = translation table (0 = no translation)
                // HL = physical sector
                if (cpu.DE == 0)
                    cpu.HL = cpu.BC; // No translation table - pass through
                else
                    cpu.HL = _mem.ReadWord((ushort)(cpu.DE + cpu.BC - 1));
                break;
        }
    }

    private void Boot(I8080 cpu)
    {
        // Cold boot: initialize zero page and start CCP
        InitZeroPage(cpu);
        cpu.PC = MemoryMap.CCP_BASE;
        cpu.SP = MemoryMap.BIOS_BASE; // safe stack area
    }

    private void WarmBoot(I8080 cpu)
    {
        // Reload CCP+BDOS from stored binary, then restart CCP
        _mem.Load(MemoryMap.CCP_BASE, _cpmBinary);
        InitZeroPage(cpu);
        // Warm boot enters CCP at +3 (past the cold-boot init vector at CCP_BASE+0).
        // The cpm22.sys header is: JMP cold (0xE400) / JMP warm (0xE403).
        cpu.PC = MemoryMap.CCP_BASE + 3;
        cpu.SP = MemoryMap.BIOS_BASE;
        _input.Clear();
    }

    private void InitZeroPage(I8080 cpu)
    {
        // 0x0000: JMP WBOOT (BIOS+3)
        ushort wboot = (ushort)(MemoryMap.BIOS_BASE + 3);
        _mem.Write(0x0000, 0xC3);
        _mem.Write(0x0001, (byte)wboot);
        _mem.Write(0x0002, (byte)(wboot >> 8));

        // 0x0003: IOBYTE (not used, set to 0)
        _mem.Write(0x0003, 0x00);

        // 0x0004: Current drive (A=0)
        _mem.Write(0x0004, 0x00);

        // 0x0005: JMP BDOS entry
        ushort bdos = (ushort)MemoryMap.BDOS_ENTRY;
        _mem.Write(0x0005, 0xC3);
        _mem.Write(0x0006, (byte)bdos);
        _mem.Write(0x0007, (byte)(bdos >> 8));
    }
}
