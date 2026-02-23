using Emulator.Bios;
using Emulator.Disk;
using System.Text;
using System.Text.RegularExpressions;

namespace Emulator.Programs;

/// <summary>
/// Two-pass Intel 8080 assembler.
/// Invoked from CCP as: ASM filename   (omit .ASM extension)
/// Reads filename.ASM from disk, assembles to filename.COM.
/// Supports all 8080 mnemonics plus ORG, DB, DW, DS, EQU, END directives.
/// </summary>
public sealed class Assembler8080
{
    private readonly TerminalInputQueue _input;
    private readonly Action<string> _output;
    private readonly CpmDisk? _disk;

    // Symbol table: label → address or EQU value
    private readonly Dictionary<string, int> _symbols = new(StringComparer.OrdinalIgnoreCase);
    // Output bytes
    private List<byte> _code = new();
    // Current origin
    private int _origin = 0x0100; // CP/M TPA default
    private int _currentAddr;
    // Error state
    private int _errorCount;
    private string _currentFile = "";

    public Assembler8080(TerminalInputQueue input, Action<string> output, CpmDisk? disk)
    {
        _input  = input;
        _output = output;
        _disk   = disk;
    }

    public void Run(string arg, CancellationToken ct)
    {
        arg = arg.Trim();
        if (arg.Length == 0)
        {
            _output("ASM: no filename specified\r\n");
            _output("Usage: ASM FILENAME  (omit .ASM)\r\n");
            return;
        }

        _currentFile = arg.ToUpperInvariant();
        if (_currentFile.EndsWith(".ASM")) _currentFile = _currentFile[..^4];

        string srcName = _currentFile + ".ASM";
        string outName = _currentFile + ".COM";

        if (_disk == null) { _output("No disk\r\n"); return; }

        // Read source file
        var (name, ext) = CpmDisk.ParseFilename(srcName);
        var srcFile = _disk.OpenFile(name, ext);
        if (srcFile == null)
        {
            _output($"ASM: file not found: {srcName}\r\n");
            return;
        }

        byte[] content = _disk.GetFileBytes(srcFile);
        string source = Encoding.ASCII.GetString(content);
        string[] lines = source.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');

        _output($"Assembling {srcName}...\r\n");

        // Pass 1: collect symbols
        _symbols.Clear();
        _code.Clear();
        _errorCount = 0;
        _origin = 0x0100;
        _currentAddr = _origin;

        foreach (var (line, lineNum) in lines.Select((l, i) => (l, i + 1)))
        {
            try { Pass1Line(line, lineNum); }
            catch (AssemblerException ex) { Error(lineNum, ex.Message); }
            catch (Exception ex) { Error(lineNum, $"Internal error: {ex.Message}"); }
        }

        if (_errorCount > 0)
        {
            _output($"Pass 1: {_errorCount} error(s). Assembly failed.\r\n");
            return;
        }

        // Pass 2: generate code
        _code.Clear();
        _currentAddr = _origin;

        foreach (var (line, lineNum) in lines.Select((l, i) => (l, i + 1)))
        {
            try { Pass2Line(line, lineNum); }
            catch (AssemblerException ex) { Error(lineNum, ex.Message); }
            catch (Exception ex) { Error(lineNum, $"Internal error: {ex.Message}"); }
        }

        if (_errorCount > 0)
        {
            _output($"Pass 2: {_errorCount} error(s). Assembly failed.\r\n");
            return;
        }

        // Write output .COM file
        if (_code.Count == 0) { _output("ASM: no output generated\r\n"); return; }

        var (oName, oExt) = CpmDisk.ParseFilename(outName);
        var outFile = _disk.OpenOrCreate(oName, oExt);
        _disk.SetFileBytes(outFile, _code.ToArray());
        _disk.FlushFile(outFile);

        _output($"ASM: {srcName} → {outName}  ({_code.Count} bytes, origin 0x{_origin:X4})\r\n");
    }

    // ── Pass 1: collect labels and calculate addresses ────────────────────────

    private void Pass1Line(string rawLine, int lineNum)
    {
        var (label, mnemonic, operands) = ParseLine(rawLine);

        // Handle labels (must start at column 0 and end with ':')
        if (label.Length > 0)
        {
            if (_symbols.ContainsKey(label))
                throw new AssemblerException($"Duplicate label: {label}");
            _symbols[label] = _currentAddr;
        }

        if (mnemonic.Length == 0) return;

        // Handle EQU specially in pass 1
        if (mnemonic.Equals("EQU", StringComparison.OrdinalIgnoreCase))
        {
            if (label.Length == 0)
                throw new AssemblerException("EQU requires a label");
            int val = EvaluateExpr(operands, pass1: true);
            _symbols[label] = val;
            return;
        }

        // Calculate size of this instruction/directive
        int size = GetInstructionSize(mnemonic, operands);
        _currentAddr += size;
    }

    // ── Pass 2: emit machine code ─────────────────────────────────────────────

    private void Pass2Line(string rawLine, int lineNum)
    {
        var (label, mnemonic, operands) = ParseLine(rawLine);

        if (mnemonic.Length == 0) return;

        switch (mnemonic.ToUpperInvariant())
        {
            case "EQU":  return; // handled in pass 1
            case "ORG":
                _origin   = EvaluateExpr(operands);
                _currentAddr = _origin;
                return;
            case "END":  return;
            case "DB":   EmitBytes(operands); return;
            case "DW":   EmitWords(operands); return;
            case "DS":   EmitSpace(operands); return;
            default:
                EmitInstruction(mnemonic, operands);
                break;
        }
    }

    // ── Line parsing ──────────────────────────────────────────────────────────

    private static (string label, string mnemonic, string operands) ParseLine(string line)
    {
        // Strip comment
        int semi = FindComment(line);
        if (semi >= 0) line = line[..semi];
        line = line.TrimEnd();
        if (line.Length == 0) return ("", "", "");

        string label = "";
        string mnemonic = "";
        string operands = "";

        // Check for label (either at start with colon, or a word at column 0 followed by whitespace)
        // Labels either end with ':' or are at column 0 followed by the rest on the same line
        if (!char.IsWhiteSpace(line[0]))
        {
            int colon = line.IndexOf(':');
            int space = FindFirstWhitespace(line);

            if (colon >= 0 && (space < 0 || colon < space))
            {
                label = line[..colon].Trim();
                line = line[(colon + 1)..].TrimStart();
            }
            else if (space > 0)
            {
                // Could be a label without colon (old-style)
                label = line[..space].Trim();
                line = line[space..].TrimStart();
            }
        }
        else
        {
            line = line.TrimStart();
        }

        if (line.Length == 0) return (label, "", "");

        // Split mnemonic from operands
        int mnEnd = FindFirstWhitespace(line);
        if (mnEnd < 0)
        {
            mnemonic = line;
        }
        else
        {
            mnemonic = line[..mnEnd];
            operands = line[mnEnd..].Trim();
        }

        return (label, mnemonic.ToUpperInvariant(), operands);
    }

    private static int FindComment(string line)
    {
        bool inStr = false;
        for (int i = 0; i < line.Length; i++)
        {
            if (line[i] == '\'' && !inStr) inStr = true;
            else if (line[i] == '\'' && inStr) inStr = false;
            else if (line[i] == ';' && !inStr) return i;
        }
        return -1;
    }

    private static int FindFirstWhitespace(string s)
    {
        for (int i = 0; i < s.Length; i++)
            if (char.IsWhiteSpace(s[i])) return i;
        return -1;
    }

    // ── Expression evaluator ──────────────────────────────────────────────────

    private int EvaluateExpr(string expr, bool pass1 = false)
    {
        expr = expr.Trim();
        if (expr.Length == 0) return 0;

        // Handle character literals: 'A'
        if (expr.Length >= 3 && expr[0] == '\'' && expr[^1] == '\'')
            return (int)expr[1];

        // Hex literal: 0FFH or 0FFh or 0xFF or FFH
        if (expr.Length >= 2 && expr[^1] is 'H' or 'h')
        {
            string hex = expr[..^1];
            if (hex.StartsWith("0x") || hex.StartsWith("0X")) hex = hex[2..];
            if (int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out int hv))
                return hv;
        }
        if (expr.StartsWith("0x") || expr.StartsWith("0X"))
        {
            if (int.TryParse(expr[2..], System.Globalization.NumberStyles.HexNumber, null, out int hv2))
                return hv2;
        }

        // Binary literal: 01010101B
        if (expr.Length >= 2 && expr[^1] is 'B' or 'b' && expr[..^1].All(c => c == '0' || c == '1'))
        {
            return Convert.ToInt32(expr[..^1], 2);
        }

        // Decimal
        if (int.TryParse(expr, out int dec)) return dec;

        // Unary minus/plus
        if (expr.StartsWith("-")) return -EvaluateExpr(expr[1..], pass1);
        if (expr.StartsWith("+")) return EvaluateExpr(expr[1..], pass1);
        if (expr.StartsWith("NOT ")) return ~EvaluateExpr(expr[4..], pass1);

        // Binary expressions (simple left-to-right, lowest precedence)
        // Try to find operator outside parentheses/strings
        int opIdx = FindOperator(expr);
        if (opIdx >= 0)
        {
            char op = expr[opIdx];
            int opLen = 1;
            // Check for two-char operators
            if (opIdx + 1 < expr.Length && expr[opIdx + 1] == '=' &&
                (op == '<' || op == '>')) opLen = 2;

            int left  = EvaluateExpr(expr[..opIdx].Trim(), pass1);
            int right = EvaluateExpr(expr[(opIdx + opLen)..].Trim(), pass1);
            return op switch
            {
                '+' => left + right,
                '-' => left - right,
                '*' => left * right,
                '/' => right != 0 ? left / right : 0,
                '%' => right != 0 ? left % right : 0,
                '&' => left & right,
                '|' => left | right,
                '^' => left ^ right,
                _ => left
            };
        }

        // Symbol lookup
        if (_symbols.TryGetValue(expr, out int symVal)) return symVal;
        if (pass1) return 0; // Unknown in pass 1 is OK (forward reference)

        throw new AssemblerException($"Undefined symbol: {expr}");
    }

    private static int FindOperator(string expr)
    {
        // Find the rightmost low-precedence operator not inside parens
        int depth = 0;
        for (int i = expr.Length - 1; i >= 0; i--)
        {
            char c = expr[i];
            if (c == ')') depth++;
            else if (c == '(') depth--;
            else if (depth == 0 && (c == '+' || c == '-') && i > 0)
                return i;
        }
        for (int i = expr.Length - 1; i >= 0; i--)
        {
            char c = expr[i];
            if (c == ')') depth++;
            else if (c == '(') depth--;
            else if (depth == 0 && (c == '*' || c == '/' || c == '%' ||
                                     c == '&' || c == '|' || c == '^'))
                return i;
        }
        return -1;
    }

    // ── Directive emitters ────────────────────────────────────────────────────

    private void EmitBytes(string operands)
    {
        foreach (var item in SplitOperands(operands))
        {
            string s = item.Trim();
            if (s.Length >= 2 && s[0] == '"' && s[^1] == '"')
            {
                // String literal
                foreach (char c in s[1..^1])
                    Emit((byte)c);
            }
            else if (s.Length >= 2 && s[0] == '\'' && s[^1] == '\'')
            {
                Emit((byte)s[1]);
            }
            else
            {
                Emit((byte)(EvaluateExpr(s) & 0xFF));
            }
        }
    }

    private void EmitWords(string operands)
    {
        foreach (var item in SplitOperands(operands))
        {
            int val = EvaluateExpr(item.Trim());
            Emit((byte)(val & 0xFF));
            Emit((byte)((val >> 8) & 0xFF));
        }
    }

    private void EmitSpace(string operands)
    {
        int count = EvaluateExpr(operands.Trim());
        for (int i = 0; i < count; i++) Emit(0);
    }

    // ── Instruction emitter ───────────────────────────────────────────────────

    private void EmitInstruction(string mn, string ops)
    {
        var args = SplitOperands(ops).Select(s => s.Trim().ToUpperInvariant()).ToArray();

        switch (mn)
        {
            // ── Data transfer ────────────────────────────────────────────────
            case "MOV":
                Emit((byte)(0x40 | (RegCode(args[0]) << 3) | RegCode(args[1])));
                break;
            case "MVI":
                Emit((byte)(0x06 | (RegCode(args[0]) << 3)));
                Emit((byte)(EvaluateExpr(ops.Split(',')[1].Trim()) & 0xFF));
                break;
            case "LXI":
                Emit((byte)(0x01 | (RpCode(args[0]) << 4)));
                EmitImm16(ops.Split(',')[1].Trim());
                break;
            case "LDA":  Emit(0x3A); EmitImm16(ops); break;
            case "STA":  Emit(0x32); EmitImm16(ops); break;
            case "LHLD": Emit(0x2A); EmitImm16(ops); break;
            case "SHLD": Emit(0x22); EmitImm16(ops); break;
            case "LDAX": Emit((byte)(0x0A | (RpCode2(args[0]) << 4))); break;
            case "STAX": Emit((byte)(0x02 | (RpCode2(args[0]) << 4))); break;
            case "XCHG": Emit(0xEB); break;

            // ── Arithmetic ───────────────────────────────────────────────────
            case "ADD": Emit((byte)(0x80 | RegCode(args[0]))); break;
            case "ADC": Emit((byte)(0x88 | RegCode(args[0]))); break;
            case "SUB": Emit((byte)(0x90 | RegCode(args[0]))); break;
            case "SBB": Emit((byte)(0x98 | RegCode(args[0]))); break;
            case "ANA": Emit((byte)(0xA0 | RegCode(args[0]))); break;
            case "XRA": Emit((byte)(0xA8 | RegCode(args[0]))); break;
            case "ORA": Emit((byte)(0xB0 | RegCode(args[0]))); break;
            case "CMP": Emit((byte)(0xB8 | RegCode(args[0]))); break;
            case "ADI": Emit(0xC6); Emit((byte)(EvaluateExpr(ops) & 0xFF)); break;
            case "ACI": Emit(0xCE); Emit((byte)(EvaluateExpr(ops) & 0xFF)); break;
            case "SUI": Emit(0xD6); Emit((byte)(EvaluateExpr(ops) & 0xFF)); break;
            case "SBI": Emit(0xDE); Emit((byte)(EvaluateExpr(ops) & 0xFF)); break;
            case "ANI": Emit(0xE6); Emit((byte)(EvaluateExpr(ops) & 0xFF)); break;
            case "XRI": Emit(0xEE); Emit((byte)(EvaluateExpr(ops) & 0xFF)); break;
            case "ORI": Emit(0xF6); Emit((byte)(EvaluateExpr(ops) & 0xFF)); break;
            case "CPI": Emit(0xFE); Emit((byte)(EvaluateExpr(ops) & 0xFF)); break;
            case "INR": Emit((byte)(0x04 | (RegCode(args[0]) << 3))); break;
            case "DCR": Emit((byte)(0x05 | (RegCode(args[0]) << 3))); break;
            case "INX": Emit((byte)(0x03 | (RpCode(args[0]) << 4))); break;
            case "DCX": Emit((byte)(0x0B | (RpCode(args[0]) << 4))); break;
            case "DAD": Emit((byte)(0x09 | (RpCode(args[0]) << 4))); break;
            case "DAA": Emit(0x27); break;
            case "CMA": Emit(0x2F); break;
            case "STC": Emit(0x37); break;
            case "CMC": Emit(0x3F); break;

            // ── Rotate ───────────────────────────────────────────────────────
            case "RLC": Emit(0x07); break;
            case "RRC": Emit(0x0F); break;
            case "RAL": Emit(0x17); break;
            case "RAR": Emit(0x1F); break;

            // ── Jumps ────────────────────────────────────────────────────────
            case "JMP": Emit(0xC3); EmitImm16(ops); break;
            case "JC":  Emit(0xDA); EmitImm16(ops); break;
            case "JNC": Emit(0xD2); EmitImm16(ops); break;
            case "JZ":  Emit(0xCA); EmitImm16(ops); break;
            case "JNZ": Emit(0xC2); EmitImm16(ops); break;
            case "JP":  Emit(0xF2); EmitImm16(ops); break;
            case "JM":  Emit(0xFA); EmitImm16(ops); break;
            case "JPE": Emit(0xEA); EmitImm16(ops); break;
            case "JPO": Emit(0xE2); EmitImm16(ops); break;
            case "PCHL":Emit(0xE9); break;

            // ── Calls / Returns ──────────────────────────────────────────────
            case "CALL": Emit(0xCD); EmitImm16(ops); break;
            case "CC":   Emit(0xDC); EmitImm16(ops); break;
            case "CNC":  Emit(0xD4); EmitImm16(ops); break;
            case "CZ":   Emit(0xCC); EmitImm16(ops); break;
            case "CNZ":  Emit(0xC4); EmitImm16(ops); break;
            case "CP":   Emit(0xF4); EmitImm16(ops); break;
            case "CM":   Emit(0xFC); EmitImm16(ops); break;
            case "CPE":  Emit(0xEC); EmitImm16(ops); break;
            case "CPO":  Emit(0xE4); EmitImm16(ops); break;
            case "RET":  Emit(0xC9); break;
            case "RC":   Emit(0xD8); break;
            case "RNC":  Emit(0xD0); break;
            case "RZ":   Emit(0xC8); break;
            case "RNZ":  Emit(0xC0); break;
            case "RP":   Emit(0xF0); break;
            case "RM":   Emit(0xF8); break;
            case "RPE":  Emit(0xE8); break;
            case "RPO":  Emit(0xE0); break;
            case "RST":
                int rst = EvaluateExpr(ops) & 7;
                Emit((byte)(0xC7 | (rst << 3)));
                break;

            // ── Stack ────────────────────────────────────────────────────────
            case "PUSH": Emit((byte)(0xC5 | (RpCode3(args[0]) << 4))); break;
            case "POP":  Emit((byte)(0xC1 | (RpCode3(args[0]) << 4))); break;
            case "XTHL": Emit(0xE3); break;
            case "SPHL": Emit(0xF9); break;

            // ── I/O ──────────────────────────────────────────────────────────
            case "IN":  Emit(0xDB); Emit((byte)(EvaluateExpr(ops) & 0xFF)); break;
            case "OUT": Emit(0xD3); Emit((byte)(EvaluateExpr(ops) & 0xFF)); break;

            // ── Misc ──────────────────────────────────────────────────────────
            case "EI":  Emit(0xFB); break;
            case "DI":  Emit(0xF3); break;
            case "HLT": Emit(0x76); break;
            case "NOP": Emit(0x00); break;

            default:
                throw new AssemblerException($"Unknown mnemonic: {mn}");
        }
    }

    // ── Size calculation for pass 1 ───────────────────────────────────────────

    private int GetInstructionSize(string mn, string ops)
    {
        return mn.ToUpperInvariant() switch
        {
            "EQU" => 0,
            "ORG" => 0,
            "END" => 0,
            "DB"  => CountDbSize(ops),
            "DW"  => SplitOperands(ops).Count * 2,
            "DS"  => EvaluateExpr(ops, pass1: true),

            // 3-byte instructions (with 16-bit operand)
            "LXI" or "LDA" or "STA" or "LHLD" or "SHLD" or
            "JMP" or "JC"  or "JNC" or "JZ" or "JNZ" or "JP" or "JM" or "JPE" or "JPO" or
            "CALL" or "CC" or "CNC" or "CZ" or "CNZ" or "CP" or "CM" or "CPE" or "CPO" => 3,

            // 2-byte instructions (with 8-bit operand)
            "MVI" or "ADI" or "ACI" or "SUI" or "SBI" or
            "ANI" or "XRI" or "ORI" or "CPI" or
            "IN"  or "OUT" or "RST" => 2,

            // 1-byte instructions
            _ => 1,
        };
    }

    private int CountDbSize(string ops)
    {
        int count = 0;
        foreach (var item in SplitOperands(ops))
        {
            string s = item.Trim();
            if (s.Length >= 2 && s[0] == '"' && s[^1] == '"')
                count += s.Length - 2; // string length
            else
                count += 1;
        }
        return count;
    }

    // ── Register encoding ─────────────────────────────────────────────────────

    private static int RegCode(string r) => r.ToUpperInvariant() switch
    {
        "B" => 0, "C" => 1, "D" => 2, "E" => 3,
        "H" => 4, "L" => 5, "M" => 6, "A" => 7,
        _ => throw new AssemblerException($"Unknown register: {r}")
    };

    private static int RpCode(string r) => r.ToUpperInvariant() switch
    {
        "B" or "BC" => 0,
        "D" or "DE" => 1,
        "H" or "HL" => 2,
        "SP" => 3,
        _ => throw new AssemblerException($"Unknown register pair: {r}")
    };

    private static int RpCode2(string r) => r.ToUpperInvariant() switch
    {
        "B" or "BC" => 0,
        "D" or "DE" => 1,
        _ => throw new AssemblerException($"LDAX/STAX needs BC or DE, got: {r}")
    };

    private static int RpCode3(string r) => r.ToUpperInvariant() switch
    {
        "B" or "BC" => 0,
        "D" or "DE" => 1,
        "H" or "HL" => 2,
        "PSW" => 3,
        _ => throw new AssemblerException($"PUSH/POP needs BC/DE/HL/PSW, got: {r}")
    };

    // ── Emit helpers ──────────────────────────────────────────────────────────

    private void Emit(byte b)
    {
        _code.Add(b);
        _currentAddr++;
    }

    private void EmitImm16(string expr)
    {
        // Handle "REG, EXPR" format for LXI etc.
        int comma = expr.IndexOf(',');
        if (comma >= 0) expr = expr[(comma + 1)..];

        int val = EvaluateExpr(expr.Trim());
        Emit((byte)(val & 0xFF));
        Emit((byte)((val >> 8) & 0xFF));
    }

    private static List<string> SplitOperands(string ops)
    {
        var result = new List<string>();
        var current = new StringBuilder();
        bool inStr = false;
        char strChar = '"';
        int depth = 0;

        foreach (char c in ops)
        {
            if (inStr)
            {
                current.Append(c);
                if (c == strChar) inStr = false;
            }
            else if (c == '\'' || c == '"')
            {
                inStr = true;
                strChar = c;
                current.Append(c);
            }
            else if (c == '(') { depth++; current.Append(c); }
            else if (c == ')') { depth--; current.Append(c); }
            else if (c == ',' && depth == 0)
            {
                result.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }
        if (current.Length > 0)
            result.Add(current.ToString());
        return result;
    }

    private void Error(int line, string message)
    {
        _output($"  Error at line {line}: {message}\r\n");
        _errorCount++;
    }
}

internal sealed class AssemblerException : Exception
{
    public AssemblerException(string message) : base(message) { }
}
