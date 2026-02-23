using Emulator.Bios;
using Emulator.CPU;
using Emulator.Disk;
using Emulator.Memory;
using Emulator.Programs;
using System.Text;

namespace Emulator.Cpm;

/// <summary>
/// CP/M 2.2 CCP (Console Command Processor) implemented in .NET.
/// Provides the interactive command prompt, built-in commands (DIR, ERA, REN, TYPE, SAVE, USER),
/// drive switching, and launching of built-in programs (EDIT, ASM, BASIC)
/// and external .COM files stored on disk.
/// </summary>
public sealed class CcpHandler
{
    private readonly TerminalInputQueue _input;
    private readonly Action<string> _output;
    private readonly BdosHandler _bdos;
    private readonly DiskSystem _diskSystem;
    private readonly MemoryBus _mem;
    private readonly CpmDisk?[] _drives;

    private int _currentDrive = 0; // 0=A, 1=B, ...

    public int CurrentDrive
    {
        get => _currentDrive;
        set { _currentDrive = Math.Clamp(value, 0, 3); _bdos.CurrentDrive = _currentDrive; }
    }

    public CcpHandler(
        TerminalInputQueue input,
        Action<string> output,
        BdosHandler bdos,
        DiskSystem diskSystem,
        MemoryBus mem,
        CpmDisk?[] drives)
    {
        _input = input;
        _output = output;
        _bdos = bdos;
        _diskSystem = diskSystem;
        _mem = mem;
        _drives = drives;
    }

    /// <summary>
    /// Run the CCP command loop. Called from BiosHandler.Boot() and BiosHandler.WarmBoot().
    /// Blocks the CPU thread while awaiting user input.
    /// Returns when the user launches a .COM program (which should then run on the CPU).
    /// </summary>
    public void Run(I8080 cpu, CancellationToken ct)
    {
        // Sync drive state
        _bdos.CurrentDrive = _currentDrive;
        _mem.Write(MemoryMap.DRIVE_ADDR, (byte)_currentDrive);

        while (!ct.IsCancellationRequested)
        {
            // Flush any dirty files before showing prompt
            _bdos.FlushAll();

            // Show prompt: "A> "
            _output($"\r\n{(char)('A' + _currentDrive)}>");

            // Read command line
            string cmdLine = ReadLine(ct).Trim();
            if (ct.IsCancellationRequested) return;
            _output("\r\n");

            if (cmdLine.Length == 0) continue;

            // Parse: command + optional argument
            int spaceIdx = cmdLine.IndexOf(' ');
            string cmd = (spaceIdx < 0 ? cmdLine : cmdLine[..spaceIdx]).ToUpperInvariant().Trim();
            string arg = spaceIdx < 0 ? "" : cmdLine[(spaceIdx + 1)..].Trim();

            // Drive change: "B:"
            if (cmd.Length == 2 && char.IsLetter(cmd[0]) && cmd[1] == ':')
            {
                int drive = cmd[0] - 'A';
                if (drive >= 0 && drive < 4 && _drives[drive] != null)
                {
                    CurrentDrive = drive;
                    _mem.Write(MemoryMap.DRIVE_ADDR, (byte)drive);
                }
                else
                {
                    _output($"Drive {cmd[0]}: not ready\r\n");
                }
                continue;
            }

            // Built-in commands
            switch (cmd)
            {
                case "DIR":
                    CmdDir(arg);
                    continue;
                case "ERA":
                case "DEL":
                case "DELETE":
                    CmdEra(arg);
                    continue;
                case "REN":
                case "RENAME":
                    CmdRen(arg);
                    continue;
                case "TYPE":
                    CmdType(arg);
                    continue;
                case "SAVE":
                    CmdSave(arg, cpu);
                    continue;
                case "USER":
                    // USER n: change user area
                    continue;
                case "CLS":
                case "CLEAR":
                    _output("\x1B[2J\x1B[H"); // ANSI clear screen
                    continue;
                case "VER":
                case "VERSION":
                    _output("CP/M 2.2 (.NET)\r\n");
                    continue;
                case "HELP":
                    CmdHelp();
                    continue;
            }

            // Built-in programs: EDIT, ASM / ASSEMBLE, BASIC
            if (cmd == "EDIT" || cmd == "ED")
            {
                var editor = new TextEditor(_input, _output, GetCurrentDisk());
                editor.Run(arg, ct);
                continue;
            }

            if (cmd == "ASM" || cmd == "ASSEMBLE")
            {
                var asm = new Assembler8080(_input, _output, GetCurrentDisk());
                asm.Run(arg, ct);
                continue;
            }

            if (cmd == "BASIC" || cmd == "MBASIC" || cmd == "BAS")
            {
                var basic = new BasicInterpreter(_input, _output, GetCurrentDisk());
                basic.Run(arg, ct);
                continue;
            }

            // Try to find and load a .COM file from the current drive
            bool launched = TryLaunchCom(cmd, arg, cpu, ct);
            if (launched) return; // CPU will now execute from TPA

            _output($"?{cmd}\r\n");
        }
    }

    // ── Built-in Commands ─────────────────────────────────────────────────────

    private void CmdDir(string arg)
    {
        // Parse optional drive prefix and wildcard
        string? driveSpec = null;
        string pattern = "*.*";

        if (arg.Length >= 2 && char.IsLetter(arg[0]) && arg[1] == ':')
        {
            driveSpec = arg[..1].ToUpperInvariant();
            arg = arg[2..];
        }

        if (arg.Length > 0) pattern = arg.ToUpperInvariant();

        int drive = driveSpec != null ? driveSpec[0] - 'A' : _currentDrive;
        var disk = drive >= 0 && drive < 4 ? _drives[drive] : null;

        if (disk == null)
        {
            _output($"Drive {(char)('A' + drive)}: not ready\r\n");
            return;
        }

        // Parse wildcard pattern
        var (namePattern, extPattern) = ParseWildcard(pattern);
        var files = disk.SearchFiles(namePattern, extPattern);

        if (files.Count == 0)
        {
            _output("No File\r\n");
            return;
        }

        // Display in 4-column format
        string driveLetter = ((char)('A' + drive)).ToString();
        int col = 0;
        foreach (var file in files)
        {
            string entry = $"{driveLetter}:{file.Name.TrimEnd(),-8}.{file.Ext.TrimEnd(),-3}";
            _output(entry);
            col++;
            if (col % 4 == 0) _output("\r\n");
            else _output("  ");
        }
        if (col % 4 != 0) _output("\r\n");
    }

    private void CmdEra(string arg)
    {
        if (arg.Length == 0) { _output("ERA: no file specified\r\n"); return; }

        var disk = GetCurrentDisk();
        if (disk == null) { _output("No disk\r\n"); return; }

        var (namePattern, extPattern) = ParseWildcard(arg.ToUpperInvariant());
        var matching = disk.SearchFiles(namePattern, extPattern);

        if (matching.Count == 0) { _output("No File\r\n"); return; }

        if (matching.Count > 1)
        {
            _output($"Erase {matching.Count} files (Y/N)? ");
            byte ans = _input.BlockingRead();
            _output(((char)ans).ToString() + "\r\n");
            if (char.ToUpperInvariant((char)ans) != 'Y') return;
        }

        int deleted = disk.DeleteFiles(namePattern, extPattern);
        _output($"{deleted} file(s) erased\r\n");
    }

    private void CmdRen(string arg)
    {
        // Syntax: REN new=old
        int eq = arg.IndexOf('=');
        if (eq < 0) { _output("REN: use REN NEW=OLD\r\n"); return; }

        string newSpec = arg[..eq].Trim().ToUpperInvariant();
        string oldSpec = arg[(eq + 1)..].Trim().ToUpperInvariant();

        var disk = GetCurrentDisk();
        if (disk == null) { _output("No disk\r\n"); return; }

        var (oldName, oldExt) = CpmDisk.ParseFilename(oldSpec);
        var (newName, newExt) = CpmDisk.ParseFilename(newSpec);

        if (!disk.RenameFile(oldName, oldExt, newName, newExt))
            _output($"File not found: {oldSpec}\r\n");
    }

    private void CmdType(string arg)
    {
        if (arg.Length == 0) { _output("TYPE: no file specified\r\n"); return; }

        var disk = GetCurrentDisk();
        if (disk == null) { _output("No disk\r\n"); return; }

        var (name, ext) = CpmDisk.ParseFilename(arg.ToUpperInvariant());
        var file = disk.OpenFile(name, ext);
        if (file == null) { _output($"File not found: {arg}\r\n"); return; }

        byte[] content = disk.GetFileBytes(file);
        var sb = new StringBuilder();
        foreach (byte b in content)
        {
            if (b == 0x1A) break; // CP/M EOF
            if (b == '\r') continue; // skip bare CR (output as \r\n on \n)
            if (b == '\n') sb.Append("\r\n");
            else sb.Append((char)b);
        }
        _output(sb.ToString());
        _output("\r\n");
    }

    private void CmdSave(string arg, I8080 cpu)
    {
        // SAVE n filename  -- saves n 256-byte pages from TPA to file
        var parts = arg.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2 || !int.TryParse(parts[0], out int pages))
        {
            _output("SAVE: use SAVE n filename\r\n");
            return;
        }

        var disk = GetCurrentDisk();
        if (disk == null) { _output("No disk\r\n"); return; }

        int size = pages * 256;
        var data = new byte[size];
        for (int i = 0; i < size; i++)
            data[i] = _mem.Read((ushort)(MemoryMap.TPA_BASE + i));

        var (name, ext) = CpmDisk.ParseFilename(parts[1].ToUpperInvariant());
        var file = disk.OpenOrCreate(name, ext);
        disk.SetFileBytes(file, data);
        disk.FlushFile(file);
        _output($"Saved {size} bytes to {parts[1].ToUpperInvariant()}\r\n");
    }

    private void CmdHelp()
    {
        _output("Built-in commands:\r\n");
        _output("  DIR [pattern]     List files\r\n");
        _output("  ERA file          Erase file(s)\r\n");
        _output("  REN new=old       Rename file\r\n");
        _output("  TYPE file         Display file contents\r\n");
        _output("  SAVE n file       Save n pages (256B) from TPA to file\r\n");
        _output("  x:                Switch to drive x (A-D)\r\n");
        _output("  CLS               Clear screen\r\n");
        _output("Built-in programs:\r\n");
        _output("  EDIT [file]       Screen editor\r\n");
        _output("  ASM file          8080 assembler\r\n");
        _output("  BASIC [file]      BASIC interpreter\r\n");
    }

    // ── .COM file loading ─────────────────────────────────────────────────────

    private bool TryLaunchCom(string name, string arg, I8080 cpu, CancellationToken ct)
    {
        var disk = GetCurrentDisk();
        if (disk == null) return false;

        // Look for NAME.COM
        var file = disk.OpenFile(name.PadRight(8)[..8], "COM");
        if (file == null) return false;

        byte[] data = disk.GetFileBytes(file);
        if (data.Length == 0) return false;

        // Load into TPA at 0x0100
        for (int i = 0; i < data.Length && i < MemoryMap.TPA_TOP - MemoryMap.TPA_BASE; i++)
            _mem.Write((ushort)(MemoryMap.TPA_BASE + i), data[i]);

        // Set up default FCB at 0x005C (FCB1) with filename
        SetupFcb(0x005C, name, arg);

        // Set up command tail at 0x0080 (DMA default)
        SetupCommandTail(arg);

        // Set CPU to start at TPA, with stack just below BIOS
        cpu.PC = MemoryMap.TPA_BASE;
        cpu.SP = (ushort)(MemoryMap.BIOS_BASE - 2);
        cpu.A = 0;
        cpu.BC = 0;
        cpu.DE = 0;
        cpu.HL = 0;

        return true; // Signal to CcpHandler.Run's caller to let the CPU execute
    }

    private void SetupFcb(ushort addr, string name, string arg)
    {
        // Clear FCB
        for (int i = 0; i < 36; i++)
            _mem.Write((ushort)(addr + i), 0);

        // Set drive = 0 (current)
        _mem.Write(addr, 0);

        // Parse first filename from arg for FCB1
        string fname = arg.Trim().Split(' ')[0].ToUpperInvariant();
        if (fname.Length == 0) return;

        var (n, e) = CpmDisk.ParseFilename(fname);
        for (int i = 0; i < 8; i++) _mem.Write((ushort)(addr + 1 + i), (byte)n[i]);
        for (int i = 0; i < 3; i++) _mem.Write((ushort)(addr + 9 + i), (byte)e[i]);
    }

    private void SetupCommandTail(string arg)
    {
        // 0x0080: length byte, then command tail chars
        byte[] argBytes = Encoding.ASCII.GetBytes(arg);
        int len = Math.Min(argBytes.Length, 125);
        _mem.Write(0x0080, (byte)len);
        for (int i = 0; i < len; i++)
            _mem.Write((ushort)(0x0081 + i), argBytes[i]);
        _mem.Write((ushort)(0x0081 + len), 0);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private string ReadLine(CancellationToken ct)
    {
        var sb = new StringBuilder(128);
        while (!ct.IsCancellationRequested)
        {
            byte ch;
            try { ch = _input.BlockingRead(ct); }
            catch { return sb.ToString(); }

            if (ch == 13 || ch == 10) // Enter
            {
                break;
            }
            else if ((ch == 8 || ch == 0x7F) && sb.Length > 0) // Backspace
            {
                sb.Length--;
                _output("\x08 \x08");
            }
            else if (ch == 3) // Ctrl+C
            {
                _output("^C");
                sb.Clear();
                break;
            }
            else if (ch == 21) // Ctrl+U: cancel
            {
                _output("X\r\n");
                sb.Clear();
                _output($"{(char)('A' + _currentDrive)}>");
            }
            else if (ch >= 32 && sb.Length < 126)
            {
                sb.Append((char)ch);
                _output(((char)ch).ToString());
            }
        }
        return sb.ToString();
    }

    private CpmDisk? GetCurrentDisk() =>
        _currentDrive < _drives.Length ? _drives[_currentDrive] : null;

    private static (string name, string ext) ParseWildcard(string pattern)
    {
        if (pattern == "*" || pattern == "*.*")
            return ("*", "*");

        int dot = pattern.IndexOf('.');
        if (dot < 0) return (pattern, "*");

        string name = dot > 0 ? pattern[..dot] : "*";
        string ext  = dot < pattern.Length - 1 ? pattern[(dot + 1)..] : "*";
        return (name, ext);
    }
}
