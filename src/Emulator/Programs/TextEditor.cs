using Emulator.Bios;
using Emulator.Disk;
using System.Text;

namespace Emulator.Programs;

/// <summary>
/// Screen-based text editor using ANSI/VT100 escape codes.
/// Invoked from the CCP as: EDIT [filename]
///
/// Key bindings:
///   Arrow keys        — move cursor
///   Ctrl+S / F2       — save file
///   Ctrl+Q / Ctrl+X   — quit (asks if unsaved)
///   Ctrl+K            — delete current line
///   Ctrl+Y            — delete to end of line
///   Enter             — insert new line
///   Backspace / Del   — delete character
///   PgUp / PgDn       — scroll a page
///   Home / End        — beginning/end of line
///   Ctrl+Home/End     — beginning/end of file
///   Ctrl+G            — go to line number
///   Ctrl+F            — find text
/// </summary>
public sealed class TextEditor
{
    private readonly TerminalInputQueue _input;
    private readonly Action<string> _output;
    private readonly CpmDisk? _disk;

    private List<string> _lines = new();
    private int _cursorLine; // 0-based line index
    private int _cursorCol;  // 0-based column index
    private int _topLine;    // first visible line (scroll offset)
    private int _leftCol;    // horizontal scroll offset

    private const int TERM_WIDTH  = 80;
    private const int TERM_HEIGHT = 24;
    private const int EDIT_ROWS   = TERM_HEIGHT - 2; // minus status bars

    private bool _dirty;
    private string _filename = "";
    private string _statusMessage = "";

    public TextEditor(TerminalInputQueue input, Action<string> output, CpmDisk? disk)
    {
        _input  = input;
        _output = output;
        _disk   = disk;
    }

    /// <summary>Run the editor. filename can be empty (new file).</summary>
    public void Run(string filename, CancellationToken ct)
    {
        _filename = filename.Trim();
        _lines = new List<string>();
        _dirty = false;
        _cursorLine = _cursorCol = _topLine = _leftCol = 0;

        // Load existing file if specified
        if (_filename.Length > 0 && _disk != null)
        {
            var (name, ext) = CpmDisk.ParseFilename(_filename.ToUpperInvariant());
            var file = _disk.OpenFile(name, ext);
            if (file != null)
            {
                byte[] content = _disk.GetFileBytes(file);
                _lines = Encoding.ASCII.GetString(content)
                    .Replace("\r\n", "\n").Replace("\r", "\n")
                    .Split('\n').ToList();
                _statusMessage = $"Opened {_filename} ({_lines.Count} lines)";
            }
            else
            {
                _statusMessage = $"New file: {_filename}";
            }
        }
        else if (_filename.Length == 0)
        {
            _statusMessage = "New buffer (no file)";
        }

        if (_lines.Count == 0) _lines.Add("");

        // Enter alternate screen buffer and hide cursor
        _output("\x1B[?1049h\x1B[?25l");

        try
        {
            Redraw();
            MainLoop(ct);
        }
        finally
        {
            // Leave alternate screen
            _output("\x1B[?1049l\x1B[?25h");
        }
    }

    // ── Main loop ─────────────────────────────────────────────────────────────

    private void MainLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            byte b;
            try { b = _input.BlockingRead(ct); }
            catch { break; }

            bool redraw = true;

            if (b == 0x1B) // Escape sequence
            {
                HandleEscapeSequence(ct);
            }
            else if (b == 19)  // Ctrl+S — save
            {
                SaveFile();
            }
            else if (b == 17 || b == 24) // Ctrl+Q or Ctrl+X — quit
            {
                if (!_dirty || ConfirmQuit(ct))
                    break;
            }
            else if (b == 11)  // Ctrl+K — delete line
            {
                DeleteLine();
                _dirty = true;
            }
            else if (b == 25)  // Ctrl+Y — delete to end of line
            {
                DeleteToEndOfLine();
                _dirty = true;
            }
            else if (b == 7)   // Ctrl+G — goto line
            {
                GotoLine(ct);
            }
            else if (b == 6)   // Ctrl+F — find
            {
                FindText(ct);
                redraw = true;
            }
            else if (b == 13)  // Enter
            {
                InsertNewLine();
                _dirty = true;
            }
            else if (b == 8 || b == 0x7F) // Backspace
            {
                DeleteBackward();
                _dirty = true;
            }
            else if (b >= 32 && b < 127)  // Printable
            {
                InsertChar((char)b);
                _dirty = true;
            }
            else
            {
                redraw = false;
            }

            if (redraw) Redraw();
        }
    }

    // ── Escape sequence handler ───────────────────────────────────────────────

    private void HandleEscapeSequence(CancellationToken ct)
    {
        byte b2;
        try { b2 = _input.BlockingRead(ct); } catch { return; }

        if (b2 != (byte)'[') return; // Only handle CSI sequences

        var seq = new StringBuilder();
        while (true)
        {
            byte c;
            try { c = _input.BlockingRead(ct); } catch { return; }
            seq.Append((char)c);
            if (c >= 64 && c <= 126) break; // final byte
        }

        string s = seq.ToString();
        switch (s)
        {
            case "A":     MoveCursor(-1, 0);  break; // Up
            case "B":     MoveCursor(1, 0);   break; // Down
            case "C":     MoveCursor(0, 1);   break; // Right
            case "D":     MoveCursor(0, -1);  break; // Left
            case "H":     // Home
                _cursorCol = 0;
                break;
            case "F":     // End
                _cursorCol = CurrentLine().Length;
                break;
            case "5~":    // PgUp
                MoveCursor(-EDIT_ROWS, 0);
                _topLine = Math.Max(0, _topLine - EDIT_ROWS);
                break;
            case "6~":    // PgDn
                MoveCursor(EDIT_ROWS, 0);
                _topLine = Math.Min(Math.Max(0, _lines.Count - EDIT_ROWS), _topLine + EDIT_ROWS);
                break;
            case "3~":    // Delete key
                DeleteForward();
                _dirty = true;
                break;
            case "1;5H":  // Ctrl+Home
            case "1~":
                _cursorLine = _cursorCol = _topLine = 0;
                break;
            case "1;5F":  // Ctrl+End
            case "4~":
                _cursorLine = _lines.Count - 1;
                _cursorCol = CurrentLine().Length;
                EnsureVisible();
                break;
        }
    }

    // ── Cursor movement ───────────────────────────────────────────────────────

    private void MoveCursor(int dLine, int dCol)
    {
        if (dLine != 0)
        {
            _cursorLine = Math.Clamp(_cursorLine + dLine, 0, _lines.Count - 1);
            _cursorCol  = Math.Min(_cursorCol, CurrentLine().Length);
        }
        if (dCol != 0)
        {
            int newCol = _cursorCol + dCol;
            if (newCol < 0 && _cursorLine > 0)
            {
                _cursorLine--;
                _cursorCol = CurrentLine().Length;
            }
            else if (newCol > CurrentLine().Length && _cursorLine < _lines.Count - 1)
            {
                _cursorLine++;
                _cursorCol = 0;
            }
            else
            {
                _cursorCol = Math.Clamp(newCol, 0, CurrentLine().Length);
            }
        }
        EnsureVisible();
    }

    private void EnsureVisible()
    {
        // Vertical scroll
        if (_cursorLine < _topLine)
            _topLine = _cursorLine;
        else if (_cursorLine >= _topLine + EDIT_ROWS)
            _topLine = _cursorLine - EDIT_ROWS + 1;

        // Horizontal scroll
        if (_cursorCol < _leftCol)
            _leftCol = _cursorCol;
        else if (_cursorCol >= _leftCol + TERM_WIDTH)
            _leftCol = _cursorCol - TERM_WIDTH + 1;
    }

    // ── Edit operations ───────────────────────────────────────────────────────

    private void InsertChar(char ch)
    {
        var line = CurrentLine();
        int col = Math.Min(_cursorCol, line.Length);
        _lines[_cursorLine] = line[..col] + ch + line[col..];
        _cursorCol = col + 1;
        EnsureVisible();
    }

    private void InsertNewLine()
    {
        var line = CurrentLine();
        int col = Math.Min(_cursorCol, line.Length);
        string before = line[..col];
        string after  = line[col..];
        _lines[_cursorLine] = before;
        _lines.Insert(_cursorLine + 1, after);
        _cursorLine++;
        _cursorCol = 0;
        EnsureVisible();
    }

    private void DeleteBackward()
    {
        if (_cursorCol > 0)
        {
            var line = CurrentLine();
            int col = Math.Min(_cursorCol, line.Length);
            _lines[_cursorLine] = line[..(col - 1)] + line[col..];
            _cursorCol = col - 1;
        }
        else if (_cursorLine > 0)
        {
            // Join with previous line
            int prevLen = _lines[_cursorLine - 1].Length;
            _lines[_cursorLine - 1] += _lines[_cursorLine];
            _lines.RemoveAt(_cursorLine);
            _cursorLine--;
            _cursorCol = prevLen;
            EnsureVisible();
        }
    }

    private void DeleteForward()
    {
        var line = CurrentLine();
        int col = Math.Min(_cursorCol, line.Length);
        if (col < line.Length)
        {
            _lines[_cursorLine] = line[..col] + line[(col + 1)..];
        }
        else if (_cursorLine < _lines.Count - 1)
        {
            // Join with next line
            _lines[_cursorLine] += _lines[_cursorLine + 1];
            _lines.RemoveAt(_cursorLine + 1);
        }
    }

    private void DeleteLine()
    {
        if (_lines.Count > 1)
        {
            _lines.RemoveAt(_cursorLine);
            if (_cursorLine >= _lines.Count) _cursorLine = _lines.Count - 1;
            _cursorCol = 0;
            EnsureVisible();
        }
        else
        {
            _lines[0] = "";
            _cursorCol = 0;
        }
    }

    private void DeleteToEndOfLine()
    {
        var line = CurrentLine();
        int col = Math.Min(_cursorCol, line.Length);
        _lines[_cursorLine] = line[..col];
    }

    // ── File operations ───────────────────────────────────────────────────────

    private void SaveFile()
    {
        if (_filename.Length == 0)
        {
            _statusMessage = "No filename (use Ctrl+W to save as)";
            Redraw();
            return;
        }
        if (_disk == null)
        {
            _statusMessage = "No disk mounted";
            Redraw();
            return;
        }

        var (name, ext) = CpmDisk.ParseFilename(_filename.ToUpperInvariant());
        var file = _disk.OpenOrCreate(name, ext);

        string content = string.Join("\r\n", _lines);
        byte[] data = Encoding.ASCII.GetBytes(content);
        _disk.SetFileBytes(file, data);
        _disk.FlushFile(file);

        _dirty = false;
        _statusMessage = $"Saved {_filename} ({_lines.Count} lines)";
        Redraw();
    }

    private bool ConfirmQuit(CancellationToken ct)
    {
        // Show confirmation in status bar
        ShowStatus("Unsaved changes! Quit anyway? (Y/N) ");
        byte ans;
        try { ans = _input.BlockingRead(ct); } catch { return true; }
        return char.ToUpperInvariant((char)ans) == 'Y';
    }

    // ── Navigation dialogs ────────────────────────────────────────────────────

    private void GotoLine(CancellationToken ct)
    {
        ShowStatus("Go to line: ");
        string input = ReadLineFromUser(ct);
        if (int.TryParse(input.Trim(), out int lineNum))
        {
            _cursorLine = Math.Clamp(lineNum - 1, 0, _lines.Count - 1);
            _cursorCol = 0;
            EnsureVisible();
            _statusMessage = $"Line {_cursorLine + 1}";
        }
        else
        {
            _statusMessage = "Invalid line number";
        }
    }

    private void FindText(CancellationToken ct)
    {
        ShowStatus("Find: ");
        string searchText = ReadLineFromUser(ct);
        if (searchText.Length == 0) return;

        // Search from current position forward
        for (int i = _cursorLine; i < _lines.Count; i++)
        {
            int startCol = i == _cursorLine ? _cursorCol + 1 : 0;
            int idx = _lines[i].IndexOf(searchText, startCol, StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
            {
                _cursorLine = i;
                _cursorCol = idx;
                EnsureVisible();
                _statusMessage = $"Found at line {i + 1}";
                return;
            }
        }
        _statusMessage = $"Not found: {searchText}";
    }

    private string ReadLineFromUser(CancellationToken ct)
    {
        var sb = new StringBuilder();
        while (!ct.IsCancellationRequested)
        {
            byte b;
            try { b = _input.BlockingRead(ct); } catch { break; }
            if (b == 13 || b == 10) break;
            if ((b == 8 || b == 0x7F) && sb.Length > 0)
            {
                sb.Length--;
                _output("\x08 \x08");
            }
            else if (b >= 32)
            {
                sb.Append((char)b);
                _output(((char)b).ToString());
            }
        }
        return sb.ToString();
    }

    // ── Drawing ───────────────────────────────────────────────────────────────

    private void Redraw()
    {
        var sb = new StringBuilder(4096);

        // Title bar (row 1)
        sb.Append("\x1B[1;1H");  // cursor to (1,1)
        sb.Append("\x1B[7m");    // reverse video
        string title = _filename.Length > 0 ? $" EDIT: {_filename}" : " EDIT: [No Name]";
        if (_dirty) title += " [+]";
        title = title.PadRight(TERM_WIDTH)[..TERM_WIDTH];
        sb.Append(title);
        sb.Append("\x1B[0m");    // normal video

        // Edit area (rows 2 to EDIT_ROWS+1)
        for (int row = 0; row < EDIT_ROWS; row++)
        {
            int lineIdx = _topLine + row;
            sb.Append($"\x1B[{row + 2};1H");
            sb.Append("\x1B[K"); // clear to end of line

            if (lineIdx < _lines.Count)
            {
                string line = _lines[lineIdx];
                // Apply horizontal scroll
                if (_leftCol < line.Length)
                    line = line[_leftCol..];
                else
                    line = "";

                // Truncate to terminal width
                if (line.Length > TERM_WIDTH) line = line[..TERM_WIDTH];

                // Escape special chars for output
                sb.Append(EscapeForTerminal(line));
            }
        }

        // Status bar (last row)
        ShowStatusInBuffer(sb, BuildStatusBar());

        // Position cursor in edit area
        int screenRow = _cursorLine - _topLine + 2; // +2 for title bar (1-based)
        int screenCol = _cursorCol - _leftCol + 1;  // 1-based
        screenRow = Math.Clamp(screenRow, 2, TERM_HEIGHT - 1);
        screenCol = Math.Clamp(screenCol, 1, TERM_WIDTH);
        sb.Append($"\x1B[{screenRow};{screenCol}H");
        sb.Append("\x1B[?25h"); // show cursor

        _output(sb.ToString());
    }

    private string BuildStatusBar()
    {
        string msg = _statusMessage.Length > 0 ? _statusMessage : BuildKeyHelp();
        string pos = $"Ln {_cursorLine + 1}/{_lines.Count}, Col {_cursorCol + 1}";
        int gap = TERM_WIDTH - pos.Length - 2;
        if (msg.Length > gap) msg = msg[..gap];
        return $" {msg.PadRight(gap)}{pos} ";
    }

    private static string BuildKeyHelp() =>
        "^S Save  ^Q Quit  ^K DelLine  ^G GoTo  ^F Find";

    private void ShowStatus(string message)
    {
        var sb = new StringBuilder();
        ShowStatusInBuffer(sb, (" " + message).PadRight(TERM_WIDTH)[..TERM_WIDTH]);
        _output(sb.ToString());
    }

    private static void ShowStatusInBuffer(StringBuilder sb, string statusLine)
    {
        sb.Append($"\x1B[{TERM_HEIGHT};1H"); // last row
        sb.Append("\x1B[7m");               // reverse video
        sb.Append(statusLine.Length > TERM_WIDTH ? statusLine[..TERM_WIDTH] : statusLine.PadRight(TERM_WIDTH));
        sb.Append("\x1B[0m");               // normal video
    }

    private static string EscapeForTerminal(string s)
    {
        // Replace control chars with spaces for safe display
        var sb = new StringBuilder(s.Length);
        foreach (char c in s)
        {
            if (c < 32) sb.Append(' ');
            else sb.Append(c);
        }
        return sb.ToString();
    }

    private string CurrentLine() =>
        _cursorLine < _lines.Count ? _lines[_cursorLine] : "";
}
