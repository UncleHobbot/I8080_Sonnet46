using Emulator.Bios;
using Emulator.CPU;
using Emulator.Disk;
using Emulator.Memory;
using System.Text;

namespace Emulator.Cpm;

/// <summary>
/// CP/M 2.2 BDOS (Basic Disk Operating System) implemented in .NET.
/// Called when an 8080 program executes OUT 17, A (port BDOS_PORT).
/// The function number is in register C; the parameter is in DE.
/// Results are returned in A (byte) or HL (word).
/// </summary>
public sealed class BdosHandler
{
    private readonly TerminalInputQueue _input;
    private readonly Action<string> _output;
    private readonly DiskSystem _diskSystem;
    private readonly MemoryBus _mem;

    // Per-drive CP/M file systems (initialized when disk is mounted)
    private CpmDisk?[] _drives = new CpmDisk?[4];

    // BDOS state
    private ushort _dmaAddress = 0x0080;  // default DMA at 0x0080
    private int    _currentDrive = 0;      // 0=A, 1=B, ...
    private int    _userArea = 0;          // user area (0-15)

    // Search state for fn 17/18 (SearchFirst/SearchNext)
    private List<CpmFile> _searchResults = new();
    private int _searchIndex;

    public int CurrentDrive
    {
        get => _currentDrive;
        set => _currentDrive = Math.Clamp(value, 0, 3);
    }

    public BdosHandler(
        TerminalInputQueue input,
        Action<string> output,
        DiskSystem diskSystem,
        MemoryBus mem)
    {
        _input      = input;
        _output     = output;
        _diskSystem = diskSystem;
        _mem        = mem;
    }

    /// <summary>Mount a CpmDisk on the specified drive (0=A, 1=B, ...).</summary>
    public void MountDrive(int drive, CpmDisk cpmDisk)
    {
        if (drive >= 0 && drive < 4)
            _drives[drive] = cpmDisk;
    }

    public void UnmountDrive(int drive)
    {
        if (drive >= 0 && drive < 4)
            _drives[drive] = null;
    }

    /// <summary>Flush all dirty files on all drives to their disk images.</summary>
    public void FlushAll()
    {
        foreach (var drive in _drives)
            drive?.Flush();
    }

    // ── Main dispatch ─────────────────────────────────────────────────────────

    /// <summary>Execute a BDOS call. C = function, DE = parameter.</summary>
    public void Execute(I8080 cpu)
    {
        int fn = cpu.C;
        cpu.A = 0;
        cpu.HL = 0;

        switch (fn)
        {
            case 0:  Fn00_SystemReset(cpu);         break;
            case 1:  Fn01_ConsoleInput(cpu);         break;
            case 2:  Fn02_ConsoleOutput(cpu);        break;
            case 3:  cpu.A = 0x1A;                  break; // READER = EOF
            case 4:  /* PUNCH - ignore */            break;
            case 5:  /* LIST - ignore */             break;
            case 6:  Fn06_DirectConsoleIO(cpu);      break;
            case 7:  cpu.A = 0x00;                  break; // Get I/O byte
            case 8:  /* Set I/O byte - ignore */     break;
            case 9:  Fn09_PrintString(cpu);          break;
            case 10: Fn10_ReadConsoleBuffer(cpu);    break;
            case 11: Fn11_ConsoleStatus(cpu);        break;
            case 12: cpu.HL = 0x0022; cpu.A = 0x22; break; // CP/M 2.2
            case 13: Fn13_ResetDisk();               break;
            case 14: Fn14_SelectDisk(cpu);           break;
            case 15: cpu.A = Fn15_OpenFile(cpu);     break;
            case 16: cpu.A = Fn16_CloseFile(cpu);    break;
            case 17: cpu.A = Fn17_SearchFirst(cpu);  break;
            case 18: cpu.A = Fn18_SearchNext(cpu);   break;
            case 19: cpu.A = Fn19_DeleteFile(cpu);   break;
            case 20: cpu.A = Fn20_ReadSeq(cpu);      break;
            case 21: cpu.A = Fn21_WriteSeq(cpu);     break;
            case 22: cpu.A = Fn22_MakeFile(cpu);     break;
            case 23: cpu.A = Fn23_RenameFile(cpu);   break;
            case 24: cpu.HL = 0x0001; cpu.A = 0x01;  break; // Login vector (drive A logged in)
            case 25: cpu.A = (byte)_currentDrive;    break; // Return current disk
            case 26: _dmaAddress = cpu.DE;            break; // Set DMA address
            case 27: /* Get ALLOC - return 0 */      break;
            case 28: /* Write protect - ignore */     break;
            case 29: cpu.HL = 0; cpu.A = 0;         break; // No read-only drives
            case 30: cpu.A = 0;                      break; // Set file attrs
            case 31: /* Get disk params */            break;
            case 32: Fn32_GetSetUserCode(cpu);       break;
            case 33: cpu.A = Fn33_ReadRandom(cpu);   break;
            case 34: cpu.A = Fn34_WriteRandom(cpu);  break;
            case 35: Fn35_ComputeFileSize(cpu);      break;
            case 36: Fn36_SetRandomRecord(cpu);      break;
            default: cpu.A = 0xFF;                   break;
        }
    }

    // ── Console I/O ───────────────────────────────────────────────────────────

    private void Fn00_SystemReset(I8080 cpu)
    {
        // Trigger WBOOT by jumping to 0x0000
        cpu.PC = 0x0000;
    }

    private void Fn01_ConsoleInput(I8080 cpu)
    {
        byte ch = _input.BlockingRead();
        if (ch == 13) ch = 13; // CR
        cpu.A = ch;
        // Echo character
        if (ch >= 32 || ch == '\r' || ch == '\n' || ch == 8 || ch == 0x7F)
            _output(((char)ch).ToString());
    }

    private void Fn02_ConsoleOutput(I8080 cpu)
    {
        char ch = (char)cpu.E;
        _output(ch == '\r' ? "\r\n" : ch.ToString());
    }

    private void Fn06_DirectConsoleIO(I8080 cpu)
    {
        byte e = cpu.E;
        if (e == 0xFF)
        {
            // Input: return char if available, 0 if not
            cpu.A = _input.HasData() ? _input.BlockingRead() : (byte)0;
        }
        else if (e == 0xFE)
        {
            // Status: 0xFF if char ready
            cpu.A = _input.HasData() ? (byte)0xFF : (byte)0x00;
        }
        else
        {
            // Output char in E
            char ch = (char)e;
            _output(ch == '\r' ? "\r\n" : ch.ToString());
        }
    }

    private void Fn09_PrintString(I8080 cpu)
    {
        ushort addr = cpu.DE;
        var sb = new StringBuilder();
        while (true)
        {
            byte ch = _mem.Read(addr++);
            if (ch == (byte)'$') break;
            if (ch == 13) sb.Append("\r\n");
            else sb.Append((char)ch);
        }
        _output(sb.ToString());
    }

    private void Fn10_ReadConsoleBuffer(I8080 cpu)
    {
        ushort bufAddr = cpu.DE;
        int maxLen = _mem.Read(bufAddr); // byte 0 = max chars
        var sb = new StringBuilder();

        _output(""); // ensure we're on a fresh state

        while (true)
        {
            byte ch = _input.BlockingRead();

            if (ch == 13 || ch == 10) // Enter
            {
                break;
            }
            else if ((ch == 8 || ch == 0x7F) && sb.Length > 0) // Backspace
            {
                sb.Length--;
                _output("\x08 \x08"); // backspace, space, backspace
            }
            else if (ch == 3) // Ctrl+C
            {
                _output("^C\r\n");
                sb.Clear();
                break;
            }
            else if (ch == 21) // Ctrl+U: cancel line
            {
                _output("\r\n");
                sb.Clear();
            }
            else if (ch >= 32 && sb.Length < maxLen)
            {
                sb.Append((char)ch);
                _output(((char)ch).ToString());
            }
        }

        // Write result to buffer: byte 1 = actual length, bytes 2+ = chars
        _mem.Write((ushort)(bufAddr + 1), (byte)sb.Length);
        for (int i = 0; i < sb.Length; i++)
            _mem.Write((ushort)(bufAddr + 2 + i), (byte)sb[i]);
    }

    private void Fn11_ConsoleStatus(I8080 cpu)
    {
        cpu.A = _input.HasData() ? (byte)0xFF : (byte)0x00;
    }

    // ── Disk management ───────────────────────────────────────────────────────

    private void Fn13_ResetDisk()
    {
        _currentDrive = 0;
    }

    private void Fn14_SelectDisk(I8080 cpu)
    {
        int drive = cpu.E;
        if (drive >= 0 && drive < 4 && _drives[drive] != null)
        {
            _currentDrive = drive;
            cpu.A = 0;
        }
        else
        {
            cpu.A = 0xFF;
        }
    }

    // ── File I/O ──────────────────────────────────────────────────────────────

    private byte Fn15_OpenFile(I8080 cpu)
    {
        var (drive, name, ext) = ReadFcbName(cpu.DE);
        var disk = GetDrive(drive);
        if (disk == null) return 0xFF;

        var file = disk.OpenFile(name, ext);
        if (file == null) return 0xFF;

        // Write back initialized FCB
        WriteFcbFile(cpu.DE, file);
        return 0; // success (directory index 0)
    }

    private byte Fn16_CloseFile(I8080 cpu)
    {
        var (drive, name, ext) = ReadFcbName(cpu.DE);
        var disk = GetDrive(drive);
        if (disk == null) return 0xFF;

        var file = disk.OpenFile(name, ext);
        if (file != null && file.Dirty)
            disk.FlushFile(file);
        return 0;
    }

    private byte Fn17_SearchFirst(I8080 cpu)
    {
        var (drive, namePattern, extPattern) = ReadFcbPattern(cpu.DE);
        var disk = GetDrive(drive);
        if (disk == null) { _searchResults.Clear(); return 0xFF; }

        _searchResults = disk.SearchFiles(namePattern.Trim(), extPattern.Trim());
        _searchIndex = 0;
        return WriteSearchResult();
    }

    private byte Fn18_SearchNext(I8080 cpu)
    {
        return WriteSearchResult();
    }

    private byte WriteSearchResult()
    {
        if (_searchIndex >= _searchResults.Count) return 0xFF;

        var file = _searchResults[_searchIndex++];
        // Fill DMA buffer with a fake directory entry
        var buf = new byte[128];
        Array.Fill(buf, (byte)0xE5);

        buf[0] = 0x00; // status = active
        string name = (file.Name.TrimEnd() + "        ")[..8];
        string ext  = (file.Ext.TrimEnd()  + "   ")[..3];
        for (int i = 0; i < 8; i++) buf[1 + i] = (byte)name[i];
        for (int i = 0; i < 3; i++) buf[9 + i] = (byte)ext[i];
        buf[12] = 0; buf[13] = 0; buf[14] = 0;
        buf[15] = (byte)Math.Min(128, file.TotalRecords % 128);

        _mem.WriteBlock(_dmaAddress, buf);
        return 0; // directory index = 0 (in DMA buffer)
    }

    private byte Fn19_DeleteFile(I8080 cpu)
    {
        var (drive, name, ext) = ReadFcbPattern(cpu.DE);
        var disk = GetDrive(drive);
        if (disk == null) return 0xFF;

        int count = disk.DeleteFiles(name.Trim(), ext.Trim());
        return count > 0 ? (byte)0 : (byte)0xFF;
    }

    private byte Fn20_ReadSeq(I8080 cpu)
    {
        var (drive, name, ext) = ReadFcbName(cpu.DE);
        var disk = GetDrive(drive);
        if (disk == null) return 1;

        var file = disk.OpenFile(name, ext);
        if (file == null) return 1;

        // Get current record from FCB
        byte cr  = _mem.Read((ushort)(cpu.DE + 32)); // CR
        byte ex  = _mem.Read((ushort)(cpu.DE + 12)); // EX
        byte s2  = _mem.Read((ushort)(cpu.DE + 14)); // S2
        int extentNum = ((s2 & 0x3F) << 5) | (ex & 0x1F);
        int recordNum = extentNum * RECORDS_PER_EXTENT + cr;

        var buf = new byte[128];
        byte result = disk.ReadRecord(file, recordNum, buf);
        if (result != 0) return 1; // EOF

        _mem.WriteBlock(_dmaAddress, buf);

        // Advance CR/EX/S2 in FCB
        AdvanceFcbRecord(cpu.DE, cr, ex, s2);
        return 0;
    }

    private byte Fn21_WriteSeq(I8080 cpu)
    {
        var (drive, name, ext) = ReadFcbName(cpu.DE);
        var disk = GetDrive(drive);
        if (disk == null) return 1;

        var file = disk.OpenFile(name, ext) ?? disk.OpenOrCreate(name, ext);

        byte cr  = _mem.Read((ushort)(cpu.DE + 32));
        byte ex  = _mem.Read((ushort)(cpu.DE + 12));
        byte s2  = _mem.Read((ushort)(cpu.DE + 14));
        int extentNum = ((s2 & 0x3F) << 5) | (ex & 0x1F);
        int recordNum = extentNum * RECORDS_PER_EXTENT + cr;

        var buf = new byte[128];
        _mem.ReadBlock(_dmaAddress, buf);

        byte result = disk.WriteRecord(file, recordNum, buf);
        if (result != 0) return result;

        AdvanceFcbRecord(cpu.DE, cr, ex, s2);
        return 0;
    }

    private byte Fn22_MakeFile(I8080 cpu)
    {
        var (drive, name, ext) = ReadFcbName(cpu.DE);
        var disk = GetDrive(drive);
        if (disk == null) return 0xFF;

        // Delete existing file with same name if present
        disk.DeleteFiles(name.Trim(), ext.Trim());

        var file = disk.CreateFile(name, ext);
        if (file == null) return 0xFF;

        WriteFcbFile(cpu.DE, file);
        return 0;
    }

    private byte Fn23_RenameFile(I8080 cpu)
    {
        // DE points to a special 16-byte rename FCB: bytes 0-15 = old name, 16-31 = new name
        ushort fcbAddr = cpu.DE;
        var (drive, oldName, oldExt) = ReadFcbName(fcbAddr);
        var (_, newName, newExt)     = ReadFcbName((ushort)(fcbAddr + 16));

        var disk = GetDrive(drive);
        if (disk == null) return 0xFF;

        return disk.RenameFile(oldName, oldExt, newName, newExt) ? (byte)0 : (byte)0xFF;
    }

    private void Fn32_GetSetUserCode(I8080 cpu)
    {
        if (cpu.E == 0xFF)
            cpu.A = (byte)_userArea; // Get
        else
            _userArea = cpu.E & 0x0F; // Set
    }

    private byte Fn33_ReadRandom(I8080 cpu)
    {
        var (drive, name, ext) = ReadFcbName(cpu.DE);
        var disk = GetDrive(drive);
        if (disk == null) return 1;
        var file = disk.OpenFile(name, ext);
        if (file == null) return 1;

        int recordNum = GetRandomRecord(cpu.DE);
        var buf = new byte[128];
        byte result = disk.ReadRecord(file, recordNum, buf);
        if (result != 0) return 1;
        _mem.WriteBlock(_dmaAddress, buf);
        return 0;
    }

    private byte Fn34_WriteRandom(I8080 cpu)
    {
        var (drive, name, ext) = ReadFcbName(cpu.DE);
        var disk = GetDrive(drive);
        if (disk == null) return 1;
        var file = disk.OpenOrCreate(name, ext);

        int recordNum = GetRandomRecord(cpu.DE);
        var buf = new byte[128];
        _mem.ReadBlock(_dmaAddress, buf);
        return disk.WriteRecord(file, recordNum, buf);
    }

    private void Fn35_ComputeFileSize(I8080 cpu)
    {
        var (drive, name, ext) = ReadFcbName(cpu.DE);
        var disk = GetDrive(drive);
        if (disk == null) return;
        var file = disk.OpenFile(name, ext);
        if (file == null) return;

        // Write total records as random record field (3 bytes at FCB+33)
        int n = file.TotalRecords;
        _mem.Write((ushort)(cpu.DE + 33), (byte)(n & 0xFF));
        _mem.Write((ushort)(cpu.DE + 34), (byte)((n >> 8) & 0xFF));
        _mem.Write((ushort)(cpu.DE + 35), (byte)((n >> 16) & 0xFF));
    }

    private void Fn36_SetRandomRecord(I8080 cpu)
    {
        byte cr = _mem.Read((ushort)(cpu.DE + 32));
        byte ex = _mem.Read((ushort)(cpu.DE + 12));
        byte s2 = _mem.Read((ushort)(cpu.DE + 14));
        int rec = ((s2 & 0x3F) << 5 | (ex & 0x1F)) * RECORDS_PER_EXTENT + cr;
        _mem.Write((ushort)(cpu.DE + 33), (byte)(rec & 0xFF));
        _mem.Write((ushort)(cpu.DE + 34), (byte)((rec >> 8) & 0xFF));
        _mem.Write((ushort)(cpu.DE + 35), (byte)((rec >> 16) & 0xFF));
    }

    // ── FCB helpers ───────────────────────────────────────────────────────────

    private const int RECORDS_PER_EXTENT = 128;

    private (int drive, string name, string ext) ReadFcbName(ushort addr)
    {
        byte driveByte = _mem.Read(addr);
        int drive = driveByte == 0 ? _currentDrive : (driveByte - 1);
        drive = Math.Clamp(drive, 0, 3);

        var sb = new StringBuilder(8);
        for (int i = 1; i <= 8; i++) sb.Append((char)(_mem.Read((ushort)(addr + i)) & 0x7F));
        string name = sb.ToString();

        sb.Clear();
        for (int i = 9; i <= 11; i++) sb.Append((char)(_mem.Read((ushort)(addr + i)) & 0x7F));
        string ext = sb.ToString();

        return (drive, name, ext);
    }

    private (int drive, string namePattern, string extPattern) ReadFcbPattern(ushort addr)
    {
        var (drive, name, ext) = ReadFcbName(addr);
        // ? chars in FCB name = wildcard
        string np = name.Replace('\0', '?');
        string ep = ext.Replace('\0', '?');
        return (drive, np, ep);
    }

    private void WriteFcbFile(ushort addr, CpmFile file)
    {
        // Initialize FCB with the opened file's info
        _mem.Write((ushort)(addr + 12), 0);  // EX = 0
        _mem.Write((ushort)(addr + 13), 0);  // S1 = 0
        _mem.Write((ushort)(addr + 14), 0);  // S2 = 0
        _mem.Write((ushort)(addr + 15), (byte)Math.Min(128, file.TotalRecords));
        _mem.Write((ushort)(addr + 32), 0);  // CR = 0

        // Clear allocation map
        for (int i = 16; i < 32; i++)
            _mem.Write((ushort)(addr + i), 0);
    }

    private void AdvanceFcbRecord(ushort fcbAddr, byte cr, byte ex, byte s2)
    {
        cr++;
        if (cr >= RECORDS_PER_EXTENT)
        {
            cr = 0;
            ex++;
            if ((ex & 0x1F) == 0) // overflow into S2
            {
                ex = 0;
                s2++;
            }
        }
        _mem.Write((ushort)(fcbAddr + 32), cr);
        _mem.Write((ushort)(fcbAddr + 12), ex);
        _mem.Write((ushort)(fcbAddr + 14), s2);
    }

    private int GetRandomRecord(ushort fcbAddr)
    {
        byte r0 = _mem.Read((ushort)(fcbAddr + 33));
        byte r1 = _mem.Read((ushort)(fcbAddr + 34));
        byte r2 = _mem.Read((ushort)(fcbAddr + 35));
        return r0 | (r1 << 8) | ((r2 & 0x3F) << 16);
    }

    private CpmDisk? GetDrive(int drive) =>
        (drive >= 0 && drive < 4) ? _drives[drive] : null;
}
