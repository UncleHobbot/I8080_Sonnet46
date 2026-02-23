using Emulator.Bios;
using Emulator.Disk;
using System.Text;

namespace Emulator.Programs;

/// <summary>
/// Classic line-numbered BASIC interpreter.
/// Invoked from CCP as: BASIC [filename.BAS]
///
/// Statements: PRINT, LET, INPUT, IF/THEN/ELSE, GOTO, GOSUB, RETURN,
///             FOR/NEXT, END, STOP, REM, DATA, READ, RESTORE, DIM,
///             POKE, CLS, SLEEP, ON GOTO/GOSUB
/// Functions: SIN, COS, TAN, ATN, SQR, ABS, INT, SGN, RND, FRE,
///            LEN, MID$, LEFT$, RIGHT$, STR$, VAL, CHR$, ASC, STRING$, SPACE$
/// Variables: A-Z (numeric), A$-Z$ (string), arrays A(n), A$(n)
/// Commands:  LIST, RUN, NEW, LOAD, SAVE, EDIT n, AUTO, RENUM
/// </summary>
public sealed class BasicInterpreter
{
    private readonly TerminalInputQueue _input;
    private readonly Action<string> _output;
    private readonly CpmDisk? _disk;

    // Program storage: line number → source text (without number)
    private SortedDictionary<int, string> _program = new();

    // Runtime state
    private Dictionary<string, double> _numVars = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, string>  _strVars = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, double[]> _numArrays = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, string[]> _strArrays = new(StringComparer.OrdinalIgnoreCase);
    private Stack<ForState>  _forStack  = new();
    private Stack<int>       _gosubStack = new();
    private List<double>     _dataList   = new();
    private int              _dataPtr;
    private Random           _rng = new();
    private CancellationToken _ct;

    // Execution state
    private List<int>  _lineOrder  = new(); // sorted line numbers
    private int        _lineIdx;            // index into _lineOrder
    private bool       _running;
    private bool       _stopped;

    public BasicInterpreter(TerminalInputQueue input, Action<string> output, CpmDisk? disk)
    {
        _input  = input;
        _output = output;
        _disk   = disk;
    }

    public void Run(string arg, CancellationToken ct)
    {
        _ct = ct;
        arg = arg.Trim();

        // Load file if specified
        if (arg.Length > 0 && _disk != null)
        {
            string fname = arg.ToUpperInvariant();
            if (!fname.Contains('.')) fname += ".BAS";
            LoadFile(fname);
        }

        Print("BASIC Interpreter 1.0\r\nReady\r\n");

        // Command loop
        while (!ct.IsCancellationRequested)
        {
            Print("Ok\r\n");
            string line = ReadLine();
            if (ct.IsCancellationRequested) break;

            line = line.Trim();
            if (line.Length == 0) continue;

            // Check if line starts with a number (program line)
            if (char.IsDigit(line[0]))
            {
                EnterProgramLine(line);
            }
            else
            {
                // Immediate command
                try
                {
                    ExecuteImmediate(line);
                }
                catch (BasicException ex)
                {
                    Print($"?{ex.Message}\r\n");
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    // ── Program line entry ────────────────────────────────────────────────────

    private void EnterProgramLine(string line)
    {
        // Parse line number
        int i = 0;
        while (i < line.Length && char.IsDigit(line[i])) i++;
        if (!int.TryParse(line[..i], out int lineNum)) return;

        string body = line[i..].Trim();
        if (body.Length == 0)
            _program.Remove(lineNum); // delete line
        else
            _program[lineNum] = body;
    }

    // ── Immediate commands ────────────────────────────────────────────────────

    private void ExecuteImmediate(string cmd)
    {
        string upper = cmd.ToUpperInvariant();

        if (upper == "NEW")
        {
            _program.Clear();
            ClearRuntime();
            Print("Ok\r\n");
            return;
        }
        if (upper.StartsWith("LIST"))
        {
            CmdList(cmd[4..].Trim());
            return;
        }
        if (upper == "RUN")
        {
            RunProgram(0);
            return;
        }
        if (upper.StartsWith("RUN ") && int.TryParse(cmd[4..].Trim(), out int runFrom))
        {
            RunProgram(runFrom);
            return;
        }
        if (upper.StartsWith("LOAD"))
        {
            string fname = ExtractFilename(cmd[4..]);
            if (_disk == null) { Print("?No disk\r\n"); return; }
            LoadFile(fname);
            return;
        }
        if (upper.StartsWith("SAVE"))
        {
            string fname = ExtractFilename(cmd[4..]);
            if (_disk == null) { Print("?No disk\r\n"); return; }
            SaveFile(fname);
            return;
        }
        if (upper == "CONT") { RunProgram(_lineIdx < _lineOrder.Count ? _lineOrder[_lineIdx] : 0); return; }
        if (upper == "CLS") { _output("\x1B[2J\x1B[H"); return; }
        if (upper == "SYSTEM" || upper == "EXIT" || upper == "BYE") throw new OperationCanceledException();
        if (upper.StartsWith("RENUM")) { RenumberProgram(); return; }

        // Try to execute as a statement
        ExecuteStatement(cmd);
    }

    private void CmdList(string arg)
    {
        int from = 0, to = int.MaxValue;
        if (arg.Length > 0)
        {
            var parts = arg.Split('-');
            if (parts.Length >= 1 && int.TryParse(parts[0].Trim(), out int f)) from = f;
            if (parts.Length >= 2 && int.TryParse(parts[1].Trim(), out int t)) to = t;
            else to = from; // LIST n shows just line n; LIST n- shows from n onwards
            if (arg.EndsWith("-")) to = int.MaxValue;
        }

        foreach (var (num, body) in _program)
        {
            if (num < from || num > to) continue;
            Print($"{num} {body}\r\n");
        }
    }

    private void RenumberProgram()
    {
        var newProg = new SortedDictionary<int, string>();
        var lineMap = new Dictionary<int, int>();
        int newNum = 10;
        foreach (var (old, body) in _program)
        {
            lineMap[old] = newNum;
            newProg[newNum] = body;
            newNum += 10;
        }
        // Update GOTO/GOSUB targets
        var updated = new SortedDictionary<int, string>();
        foreach (var (num, body) in newProg)
        {
            string newBody = body;
            foreach (var (old, nw) in lineMap)
                newBody = ReplaceLineRefs(newBody, old, nw);
            updated[num] = newBody;
        }
        _program = updated;
        Print("Ok\r\n");
    }

    private static string ReplaceLineRefs(string s, int old, int nw)
    {
        // Very naive replacement: only works if line number appears alone
        return s.Replace($" {old}", $" {nw}").Replace($"\t{old}", $"\t{nw}");
    }

    // ── Program execution ─────────────────────────────────────────────────────

    private void RunProgram(int startLine)
    {
        _lineOrder = _program.Keys.ToList();
        if (_lineOrder.Count == 0) return;

        ClearRuntime();
        BuildDataList();

        // Find start index
        _lineIdx = 0;
        if (startLine > 0)
        {
            _lineIdx = _lineOrder.IndexOf(startLine);
            if (_lineIdx < 0) _lineIdx = 0;
        }

        _running = true;
        _stopped = false;

        try
        {
            while (_running && _lineIdx < _lineOrder.Count && !_ct.IsCancellationRequested)
            {
                int lineNum = _lineOrder[_lineIdx];
                string stmt = _program[lineNum];
                _lineIdx++;

                ExecuteStatement(stmt);
            }
        }
        catch (BasicException ex)
        {
            int lineNum = _lineIdx > 0 && _lineIdx <= _lineOrder.Count
                ? _lineOrder[_lineIdx - 1] : 0;
            Print($"\r\n?{ex.Message} in line {lineNum}\r\n");
        }
        catch (GotoException g)
        {
            // Should be caught within ExecuteStatement
            Print($"\r\n?GOTO target not found: {g.Target}\r\n");
        }

        _running = false;
        if (!_stopped) Print("\r\nReady\r\n");
    }

    // ── Statement executor ────────────────────────────────────────────────────

    private void ExecuteStatement(string stmt)
    {
        stmt = stmt.Trim();
        if (stmt.Length == 0) return;

        // Handle multiple statements separated by ':'
        var statements = SplitStatements(stmt);
        foreach (var s in statements)
            ExecuteSingleStatement(s.Trim());
    }

    private void ExecuteSingleStatement(string stmt)
    {
        if (stmt.Length == 0) return;

        // Extract keyword
        int kEnd = 0;
        while (kEnd < stmt.Length && char.IsLetter(stmt[kEnd])) kEnd++;
        string kw = stmt[..kEnd].ToUpperInvariant();
        string rest = kEnd < stmt.Length ? stmt[kEnd..].TrimStart() : "";

        switch (kw)
        {
            case "REM":   return; // comment
            case "LET":   ExecLet(rest); break;
            case "PRINT": ExecPrint(rest); break;
            case "?":     ExecPrint(rest); break;
            case "INPUT": ExecInput(rest); break;
            case "IF":    ExecIf(rest); break;
            case "GOTO":  ExecGoto(rest); break;
            case "GO":    // GO TO
                if (rest.ToUpperInvariant().StartsWith("TO "))
                    ExecGoto(rest[3..]);
                else if (rest.ToUpperInvariant().StartsWith("SUB "))
                    ExecGosub(rest[4..]);
                break;
            case "GOSUB": ExecGosub(rest); break;
            case "RETURN":ExecReturn(); break;
            case "FOR":   ExecFor(rest); break;
            case "NEXT":  ExecNext(rest); break;
            case "END":   _running = false; break;
            case "STOP":  _running = false; _stopped = true; Print("\r\nBreak\r\n"); break;
            case "READ":  ExecRead(rest); break;
            case "RESTORE": _dataPtr = 0; break;
            case "DIM":   ExecDim(rest); break;
            case "POKE":  break; // no-op (no real memory access from BASIC)
            case "CLS":   _output("\x1B[2J\x1B[H"); break;
            case "SLEEP":
                if (double.TryParse(rest, out double secs))
                    System.Threading.Thread.Sleep((int)(secs * 1000));
                break;
            case "ON":    ExecOn(rest); break;
            case "DEF":   return; // DEF FN not implemented
            default:
                // Try assignment without LET keyword: A = expr
                if (kEnd < stmt.Length && stmt[kEnd] == '(')
                {
                    // Array assignment: A(x) = expr
                    ExecLet(stmt);
                }
                else if (kEnd < stmt.Length && stmt[kEnd] == '$' && kEnd + 1 < stmt.Length)
                {
                    ExecLet(stmt); // string var
                }
                else
                {
                    // Try implicit LET
                    int eq = stmt.IndexOf('=');
                    if (eq > 0) ExecLet(stmt);
                    else throw new BasicException($"Syntax error: {kw}");
                }
                break;
        }
    }

    // ── Statement implementations ─────────────────────────────────────────────

    private void ExecLet(string stmt)
    {
        int eq = stmt.IndexOf('=');
        if (eq < 0) throw new BasicException("Syntax error (LET)");
        string varPart = stmt[..eq].Trim();
        string valPart = stmt[(eq + 1)..].Trim();

        if (varPart.Contains('('))
        {
            // Array element
            int parenOpen = varPart.IndexOf('(');
            int parenClose = varPart.LastIndexOf(')');
            string arrName = varPart[..parenOpen].Trim();
            string idxExpr = varPart[(parenOpen + 1)..parenClose];
            int idx = (int)EvalNum(idxExpr);

            if (arrName.EndsWith("$"))
            {
                if (!_strArrays.TryGetValue(arrName, out var arr))
                    throw new BasicException($"Array not dimensioned: {arrName}");
                if (idx < 0 || idx >= arr.Length) throw new BasicException("Subscript out of range");
                arr[idx] = EvalStr(valPart);
            }
            else
            {
                if (!_numArrays.TryGetValue(arrName, out var arr))
                    throw new BasicException($"Array not dimensioned: {arrName}");
                if (idx < 0 || idx >= arr.Length) throw new BasicException("Subscript out of range");
                arr[idx] = EvalNum(valPart);
            }
        }
        else if (varPart.EndsWith("$"))
        {
            _strVars[varPart] = EvalStr(valPart);
        }
        else
        {
            _numVars[varPart] = EvalNum(valPart);
        }
    }

    private void ExecPrint(string stmt)
    {
        if (stmt.Length == 0) { Print("\r\n"); return; }

        // Split by ; (no-space) and , (tab stop)
        var parts = SplitPrint(stmt);
        bool noNewline = false;

        for (int i = 0; i < parts.Count; i++)
        {
            var (sep, expr) = parts[i];
            if (i > 0 && sep == ',')
            {
                // Tab to next 14-char stop
                Print("\t");
            }

            if (expr.Length > 0)
            {
                if (IsStringExpr(expr))
                    Print(EvalStr(expr));
                else
                    Print(FormatNumber(EvalNum(expr)));
            }

            if (i == parts.Count - 1 && (sep == ';' || sep == ','))
                noNewline = true;
        }

        if (!noNewline) Print("\r\n");
    }

    private void ExecInput(string stmt)
    {
        // INPUT [prompt ;] varlist
        string prompt = "? ";
        if (stmt.Contains(';'))
        {
            int semi = stmt.IndexOf(';');
            prompt = EvalStr(stmt[..semi].Trim()) + " ";
            stmt = stmt[(semi + 1)..].Trim();
        }
        else if (stmt.Contains(','))
        {
            int comma = stmt.IndexOf(',');
            prompt = EvalStr(stmt[..comma].Trim()) + " ";
            stmt = stmt[(comma + 1)..].Trim();
        }

        var vars = stmt.Split(',').Select(v => v.Trim()).ToList();
        foreach (var v in vars)
        {
            Print(prompt);
            string inp = ReadLine().Trim();
            if (v.EndsWith("$"))
                _strVars[v] = inp;
            else if (double.TryParse(inp, out double num))
                _numVars[v] = num;
            else
            {
                Print("?Redo from start\r\n");
                _numVars[v] = 0;
            }
        }
    }

    private void ExecIf(string stmt)
    {
        // IF cond THEN stmt/linenum [ELSE stmt/linenum]
        int thenIdx = stmt.IndexOf("THEN", StringComparison.OrdinalIgnoreCase);
        if (thenIdx < 0) throw new BasicException("IF without THEN");

        string condStr = stmt[..thenIdx].Trim();
        string thenRest = stmt[(thenIdx + 4)..].Trim();

        // Split off ELSE
        string thenPart = thenRest;
        string? elsePart = null;
        int elseIdx = FindElse(thenRest);
        if (elseIdx >= 0)
        {
            thenPart = thenRest[..elseIdx].Trim();
            elsePart = thenRest[(elseIdx + 4)..].Trim();
        }

        bool cond = EvalBool(condStr);
        string? toExecute = cond ? thenPart : elsePart;

        if (toExecute != null && toExecute.Length > 0)
        {
            if (int.TryParse(toExecute, out int lineNum))
                ExecGoto(toExecute);
            else
                ExecuteStatement(toExecute);
        }
    }

    private static int FindElse(string s)
    {
        // Find ELSE not inside strings or parens
        int depth = 0;
        bool inStr = false;
        for (int i = 0; i < s.Length - 3; i++)
        {
            if (s[i] == '"') inStr = !inStr;
            if (inStr) continue;
            if (s[i] == '(') depth++;
            else if (s[i] == ')') depth--;
            else if (depth == 0 && s.Substring(i, 4).Equals("ELSE", StringComparison.OrdinalIgnoreCase))
                return i;
        }
        return -1;
    }

    private void ExecGoto(string stmt)
    {
        int lineNum = (int)EvalNum(stmt.Trim());
        JumpToLine(lineNum);
    }

    private void ExecGosub(string stmt)
    {
        int lineNum = (int)EvalNum(stmt.Trim());
        _gosubStack.Push(_lineIdx); // save return position
        JumpToLine(lineNum);
    }

    private void ExecReturn()
    {
        if (_gosubStack.Count == 0) throw new BasicException("RETURN without GOSUB");
        _lineIdx = _gosubStack.Pop();
    }

    private void ExecFor(string stmt)
    {
        // FOR var = from TO to [STEP step]
        int eq = stmt.IndexOf('=');
        if (eq < 0) throw new BasicException("FOR: missing =");

        string varName = stmt[..eq].Trim();
        string rest = stmt[(eq + 1)..].Trim();

        int toIdx = rest.IndexOf("TO", StringComparison.OrdinalIgnoreCase);
        if (toIdx < 0) throw new BasicException("FOR: missing TO");

        string fromStr = rest[..toIdx].Trim();
        rest = rest[(toIdx + 2)..].Trim();

        double from, to, step = 1;
        from = EvalNum(fromStr);

        int stepIdx = rest.IndexOf("STEP", StringComparison.OrdinalIgnoreCase);
        if (stepIdx >= 0)
        {
            to   = EvalNum(rest[..stepIdx].Trim());
            step = EvalNum(rest[(stepIdx + 4)..].Trim());
        }
        else
        {
            to = EvalNum(rest.Trim());
        }

        _numVars[varName] = from;

        // Remove any existing FOR with same var
        while (_forStack.Count > 0 && _forStack.Peek().Var.Equals(varName, StringComparison.OrdinalIgnoreCase))
            _forStack.Pop();

        _forStack.Push(new ForState
        {
            Var      = varName,
            To       = to,
            Step     = step,
            BodyIdx  = _lineIdx // index of first line after FOR
        });
    }

    private void ExecNext(string stmt)
    {
        string varName = stmt.Trim();

        if (_forStack.Count == 0) throw new BasicException("NEXT without FOR");

        // Find matching FOR (by variable name or top of stack)
        ForState? frame = null;
        if (varName.Length == 0)
        {
            frame = _forStack.Peek();
        }
        else
        {
            foreach (var f in _forStack)
            {
                if (f.Var.Equals(varName, StringComparison.OrdinalIgnoreCase))
                { frame = f; break; }
            }
        }

        if (frame == null) throw new BasicException($"NEXT without FOR ({varName})");

        double val = _numVars.GetValueOrDefault(frame.Var) + frame.Step;
        _numVars[frame.Var] = val;

        bool done = frame.Step > 0 ? val > frame.To : val < frame.To;
        if (done)
        {
            _forStack.TryPop(out _);
        }
        else
        {
            _lineIdx = frame.BodyIdx; // jump back to body
        }
    }

    private void ExecRead(string stmt)
    {
        foreach (var v in stmt.Split(',').Select(x => x.Trim()))
        {
            if (_dataPtr >= _dataList.Count) throw new BasicException("Out of DATA");
            if (v.EndsWith("$")) _strVars[v] = _dataList[_dataPtr++].ToString();
            else _numVars[v] = _dataList[_dataPtr++];
        }
    }

    private void ExecDim(string stmt)
    {
        foreach (var spec in SplitStatements(stmt))
        {
            string s = spec.Trim();
            int parenOpen = s.IndexOf('(');
            if (parenOpen < 0) continue;
            int parenClose = s.LastIndexOf(')');
            string varName = s[..parenOpen].Trim();
            int size = (int)EvalNum(s[(parenOpen + 1)..parenClose]) + 1;

            if (varName.EndsWith("$"))
                _strArrays[varName] = new string[size];
            else
                _numArrays[varName] = new double[size];
        }
    }

    private void ExecOn(string stmt)
    {
        // ON expr GOTO/GOSUB linenum1, linenum2, ...
        int gotoIdx = stmt.IndexOf("GOTO", StringComparison.OrdinalIgnoreCase);
        int gosubIdx = stmt.IndexOf("GOSUB", StringComparison.OrdinalIgnoreCase);

        bool isGosub = gosubIdx >= 0 && (gotoIdx < 0 || gosubIdx < gotoIdx);
        int keyIdx = isGosub ? gosubIdx : gotoIdx;
        if (keyIdx < 0) throw new BasicException("ON: missing GOTO/GOSUB");

        string exprStr = stmt[..keyIdx].Trim();
        string lineList = stmt[(keyIdx + (isGosub ? 5 : 4))..].Trim();
        int n = (int)EvalNum(exprStr);

        var lines = lineList.Split(',').Select(s => s.Trim()).ToList();
        if (n < 1 || n > lines.Count) return; // out of range = no action

        string target = lines[n - 1];
        if (isGosub) ExecGosub(target);
        else ExecGoto(target);
    }

    private void JumpToLine(int lineNum)
    {
        int idx = _lineOrder.IndexOf(lineNum);
        if (idx < 0) throw new GotoException(lineNum);
        _lineIdx = idx;
    }

    // ── Expression evaluator ──────────────────────────────────────────────────

    private double EvalNum(string expr)
    {
        expr = expr.Trim();
        if (expr.Length == 0) return 0;

        // Parse in operator precedence order using a recursive descent parser
        return ParseOr(expr.AsSpan(), out _);
    }

    private double ParseOr(ReadOnlySpan<char> s, out int consumed)
    {
        double left = ParseAnd(s, out int c);
        s = s[c..];
        consumed = c;

        while (s.StartsWith("OR", StringComparison.OrdinalIgnoreCase))
        {
            s = s[2..].TrimStart();
            double right = ParseAnd(s, out int c2);
            s = s[c2..]; consumed += 2 + c2;
            left = (left != 0 || right != 0) ? -1 : 0;
        }
        return left;
    }

    private double ParseAnd(ReadOnlySpan<char> s, out int consumed)
    {
        double left = ParseNot(s, out int c);
        s = s[c..];
        consumed = c;

        while (s.StartsWith("AND", StringComparison.OrdinalIgnoreCase))
        {
            s = s[3..].TrimStart();
            double right = ParseNot(s, out int c2);
            s = s[c2..]; consumed += 3 + c2;
            left = (left != 0 && right != 0) ? -1 : 0;
        }
        return left;
    }

    private double ParseNot(ReadOnlySpan<char> s, out int consumed)
    {
        if (s.StartsWith("NOT", StringComparison.OrdinalIgnoreCase))
        {
            s = s[3..].TrimStart();
            double v = ParseComparison(s, out consumed);
            consumed += 3;
            return v == 0 ? -1 : 0;
        }
        return ParseComparison(s, out consumed);
    }

    private double ParseComparison(ReadOnlySpan<char> s, out int consumed)
    {
        double left = ParseAddSub(s, out consumed);

        while (consumed < s.Length)
        {
            string op = "";
            if (s[consumed..].StartsWith("<>")) op = "<>";
            else if (s[consumed..].StartsWith("<=")) op = "<=";
            else if (s[consumed..].StartsWith(">=")) op = ">=";
            else if (s[consumed] == '<') op = "<";
            else if (s[consumed] == '>') op = ">";
            else if (s[consumed] == '=') op = "=";
            else break;

            int opLen = op.Length;
            var rightSpan = s[(consumed + opLen)..].TrimStart();
            double right = ParseAddSub(rightSpan, out int c2);
            int skip = s.Length - rightSpan.Length + c2; // offset advancement
            consumed += skip;

            left = op switch
            {
                "="  => left == right ? -1 : 0,
                "<>" => left != right ? -1 : 0,
                "<"  => left < right  ? -1 : 0,
                ">"  => left > right  ? -1 : 0,
                "<=" => left <= right ? -1 : 0,
                ">=" => left >= right ? -1 : 0,
                _    => 0
            };
        }

        return left;
    }

    private double ParseAddSub(ReadOnlySpan<char> s, out int consumed)
    {
        double left = ParseMulDiv(s, out consumed);

        while (consumed < s.Length && (s[consumed] == '+' || s[consumed] == '-'))
        {
            char op = s[consumed];
            var rightSpan = s[(consumed + 1)..].TrimStart();
            double right = ParseMulDiv(rightSpan, out int c2);
            consumed += 1 + (s.Length - consumed - 1 - rightSpan.Length + c2);
            left = op == '+' ? left + right : left - right;
        }

        return left;
    }

    private double ParseMulDiv(ReadOnlySpan<char> s, out int consumed)
    {
        double left = ParseUnary(s, out consumed);

        while (consumed < s.Length && (s[consumed] == '*' || s[consumed] == '/' || s[consumed] == '\\' || s[consumed] == '^'))
        {
            char op = s[consumed];
            var rightSpan = s[(consumed + 1)..].TrimStart();
            double right = ParseUnary(rightSpan, out int c2);
            consumed += 1 + (s.Length - consumed - 1 - rightSpan.Length + c2);
            left = op switch
            {
                '*' => left * right,
                '/' => right != 0 ? left / right : throw new BasicException("Division by zero"),
                '\\' => right != 0 ? Math.Truncate(left / right) : throw new BasicException("Division by zero"),
                '^' => Math.Pow(left, right),
                _   => left
            };
        }

        return left;
    }

    private double ParseUnary(ReadOnlySpan<char> s, out int consumed)
    {
        s = s.TrimStart();
        consumed = 0;

        if (s.Length == 0) throw new BasicException("Expression expected");

        if (s[0] == '-')
        {
            double v = ParsePrimary(s[1..].TrimStart(), out consumed);
            consumed += 1;
            return -v;
        }
        if (s[0] == '+')
        {
            double v = ParsePrimary(s[1..].TrimStart(), out consumed);
            consumed += 1;
            return v;
        }

        return ParsePrimary(s, out consumed);
    }

    private double ParsePrimary(ReadOnlySpan<char> s, out int consumed)
    {
        // Use string for easier parsing
        string str = s.ToString().TrimStart();
        int skip = s.Length - str.Length;

        double result = ParsePrimaryStr(str, out int c);
        consumed = skip + c;
        return result;
    }

    private double ParsePrimaryStr(string s, out int consumed)
    {
        s = s.TrimStart();
        consumed = 0;

        if (s.Length == 0) throw new BasicException("Expression expected");

        // Parenthesized expression
        if (s[0] == '(')
        {
            int end = FindMatchingParen(s, 0);
            string inner = s[1..end];
            double val = EvalNum(inner);
            consumed = end + 1;
            return val;
        }

        // Numeric literal
        if (char.IsDigit(s[0]) || (s[0] == '.' && s.Length > 1 && char.IsDigit(s[1])))
        {
            int i = 0;
            while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '.' || s[i] == 'E' || s[i] == 'e' ||
                   (s[i] == '-' && i > 0 && (s[i - 1] == 'E' || s[i - 1] == 'e'))))
                i++;
            if (double.TryParse(s[..i], System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out double num))
            {
                consumed = i;
                return num;
            }
        }

        // Hex literal: &HFFFF
        if (s.StartsWith("&H", StringComparison.OrdinalIgnoreCase))
        {
            int i = 2;
            while (i < s.Length && Uri.IsHexDigit(s[i])) i++;
            consumed = i;
            return Convert.ToInt64(s[2..i], 16);
        }

        // Functions
        foreach (var fn in _numFunctions)
        {
            if (s.StartsWith(fn, StringComparison.OrdinalIgnoreCase))
            {
                int parenStart = fn.Length;
                while (parenStart < s.Length && s[parenStart] == ' ') parenStart++;
                if (parenStart < s.Length && s[parenStart] == '(')
                {
                    int end = FindMatchingParen(s, parenStart);
                    string argStr = s[(parenStart + 1)..end];
                    double fnVal = CallNumFunction(fn.ToUpperInvariant(), argStr);
                    consumed = end + 1;
                    return fnVal;
                }
            }
        }

        // String functions that return numbers: LEN, ASC, VAL, INSTR
        if (s.StartsWith("LEN(", StringComparison.OrdinalIgnoreCase))
        {
            int end = FindMatchingParen(s, 3);
            consumed = end + 1;
            return EvalStr(s[4..end]).Length;
        }
        if (s.StartsWith("ASC(", StringComparison.OrdinalIgnoreCase))
        {
            int end = FindMatchingParen(s, 3);
            string ss = EvalStr(s[4..end]);
            consumed = end + 1;
            return ss.Length > 0 ? (double)ss[0] : 0;
        }
        if (s.StartsWith("VAL(", StringComparison.OrdinalIgnoreCase))
        {
            int end = FindMatchingParen(s, 3);
            string ss = EvalStr(s[4..end]).Trim();
            consumed = end + 1;
            return double.TryParse(ss, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out double vv) ? vv : 0;
        }

        // Variable or array element
        int nameEnd = 0;
        while (nameEnd < s.Length && (char.IsLetterOrDigit(s[nameEnd]) || s[nameEnd] == '$'))
            nameEnd++;

        if (nameEnd == 0) throw new BasicException($"Unexpected: '{s[0]}'");

        string varName = s[..nameEnd];
        consumed = nameEnd;

        // Array element
        if (consumed < s.Length && s[consumed] == '(')
        {
            int end = FindMatchingParen(s, consumed);
            int idx = (int)EvalNum(s[(consumed + 1)..end]);
            consumed = end + 1;

            if (_numArrays.TryGetValue(varName, out var arr))
            {
                if (idx < 0 || idx >= arr.Length) throw new BasicException("Subscript out of range");
                return arr[idx];
            }
            throw new BasicException($"Array not found: {varName}");
        }

        return _numVars.GetValueOrDefault(varName, 0.0);
    }

    private static readonly string[] _numFunctions =
        ["SIN", "COS", "TAN", "ATN", "SQR", "ABS", "INT", "SGN", "RND", "FRE",
         "EXP", "LOG", "CINT", "CSNG"];

    private double CallNumFunction(string fn, string argStr)
    {
        double arg = argStr.Length > 0 ? EvalNum(argStr) : 0;
        return fn switch
        {
            "SIN" => Math.Sin(arg),
            "COS" => Math.Cos(arg),
            "TAN" => Math.Tan(arg),
            "ATN" => Math.Atan(arg),
            "SQR" => arg >= 0 ? Math.Sqrt(arg) : throw new BasicException("SQR of negative"),
            "ABS" => Math.Abs(arg),
            "INT" => Math.Floor(arg),
            "SGN" => Math.Sign(arg),
            "RND" => arg < 0 ? _rng.NextDouble() : _rng.NextDouble(),
            "EXP" => Math.Exp(arg),
            "LOG" => arg > 0 ? Math.Log(arg) : throw new BasicException("LOG of non-positive"),
            "FRE" => 32768,
            "CINT" or "CSNG" => Math.Round(arg),
            _ => 0
        };
    }

    private string EvalStr(string expr)
    {
        expr = expr.Trim();
        if (expr.Length == 0) return "";

        // String literal
        if (expr[0] == '"')
        {
            int end = expr.IndexOf('"', 1);
            return end < 0 ? expr[1..] : expr[1..end];
        }

        // String concatenation
        // Split by + not inside strings/parens
        int plusIdx = FindPlusOutside(expr);
        if (plusIdx >= 0)
        {
            return EvalStr(expr[..plusIdx].TrimEnd()) + EvalStr(expr[(plusIdx + 1)..].TrimStart());
        }

        // String functions
        if (expr.StartsWith("LEFT$(", StringComparison.OrdinalIgnoreCase))
        {
            var (s, n) = ParseStrNumArgs(expr[6..^1]);
            return s.Length <= n ? s : s[..(int)n];
        }
        if (expr.StartsWith("RIGHT$(", StringComparison.OrdinalIgnoreCase))
        {
            var (s, n) = ParseStrNumArgs(expr[7..^1]);
            int start = Math.Max(0, s.Length - (int)n);
            return s[start..];
        }
        if (expr.StartsWith("MID$(", StringComparison.OrdinalIgnoreCase))
        {
            var args = SplitFuncArgs(expr[5..^1]);
            string ss = EvalStr(args[0]);
            int start = args.Count >= 2 ? Math.Max(1, (int)EvalNum(args[1])) - 1 : 0;
            int len   = args.Count >= 3 ? (int)EvalNum(args[2]) : ss.Length;
            if (start >= ss.Length) return "";
            return ss.Substring(start, Math.Min(len, ss.Length - start));
        }
        if (expr.StartsWith("CHR$(", StringComparison.OrdinalIgnoreCase))
        {
            int end = expr.IndexOf(')');
            double n = EvalNum(expr[5..end]);
            return ((char)(int)n).ToString();
        }
        if (expr.StartsWith("STR$(", StringComparison.OrdinalIgnoreCase))
        {
            int end = expr.IndexOf(')');
            double n = EvalNum(expr[5..end]);
            return FormatNumber(n);
        }
        if (expr.StartsWith("SPACE$(", StringComparison.OrdinalIgnoreCase))
        {
            int end = expr.IndexOf(')');
            int n = (int)EvalNum(expr[7..end]);
            return new string(' ', Math.Max(0, n));
        }
        if (expr.StartsWith("STRING$(", StringComparison.OrdinalIgnoreCase))
        {
            var args = SplitFuncArgs(expr[8..^1]);
            int n = (int)EvalNum(args[0]);
            string fill = args.Count >= 2 && args[1].Contains('"')
                ? EvalStr(args[1])
                : ((char)(int)EvalNum(args[1])).ToString();
            char fc = fill.Length > 0 ? fill[0] : ' ';
            return new string(fc, Math.Max(0, n));
        }
        if (expr.StartsWith("LTRIM$(", StringComparison.OrdinalIgnoreCase))
        {
            int end = expr.IndexOf(')');
            return EvalStr(expr[7..end]).TrimStart();
        }
        if (expr.StartsWith("RTRIM$(", StringComparison.OrdinalIgnoreCase))
        {
            int end = expr.IndexOf(')');
            return EvalStr(expr[7..end]).TrimEnd();
        }

        // String variable or array element
        if (expr.Contains('(') && expr.Contains(')'))
        {
            int parenOpen = expr.IndexOf('(');
            string arrName = expr[..parenOpen].Trim();
            int parenClose = expr.LastIndexOf(')');
            int idx = (int)EvalNum(expr[(parenOpen + 1)..parenClose]);
            if (_strArrays.TryGetValue(arrName, out var arr))
            {
                if (idx < 0 || idx >= arr.Length) throw new BasicException("Subscript out of range");
                return arr[idx] ?? "";
            }
        }

        return _strVars.GetValueOrDefault(expr, "");
    }

    private bool EvalBool(string expr)
    {
        // Evaluates numeric expression; non-zero = true
        return EvalNum(expr) != 0;
    }

    // ── Parsing helpers ───────────────────────────────────────────────────────

    private static List<(char sep, string expr)> SplitPrint(string stmt)
    {
        var result = new List<(char sep, string expr)>();
        var current = new StringBuilder();
        char sep = '\0';
        bool inStr = false;
        int depth = 0;

        for (int i = 0; i < stmt.Length; i++)
        {
            char c = stmt[i];
            if (c == '"') inStr = !inStr;
            if (inStr) { current.Append(c); continue; }
            if (c == '(') depth++;
            else if (c == ')') depth--;
            else if ((c == ';' || c == ',') && depth == 0)
            {
                result.Add((sep, current.ToString().Trim()));
                current.Clear();
                sep = c;
                continue;
            }
            current.Append(c);
        }
        result.Add((sep, current.ToString().Trim()));
        return result;
    }

    private static List<string> SplitStatements(string s)
    {
        var result = new List<string>();
        var current = new StringBuilder();
        bool inStr = false;
        int depth = 0;

        foreach (char c in s)
        {
            if (c == '"') inStr = !inStr;
            if (inStr) { current.Append(c); continue; }
            if (c == '(') depth++;
            else if (c == ')') depth--;
            else if (c == ':' && depth == 0)
            {
                result.Add(current.ToString());
                current.Clear();
                continue;
            }
            current.Append(c);
        }
        result.Add(current.ToString());
        return result;
    }

    private static bool IsStringExpr(string s)
    {
        s = s.Trim();
        if (s.StartsWith('"')) return true;
        if (s.EndsWith("$")) return true;
        string upper = s.ToUpperInvariant();
        return upper.StartsWith("CHR$(") || upper.StartsWith("STR$(") ||
               upper.StartsWith("MID$(") || upper.StartsWith("LEFT$(") ||
               upper.StartsWith("RIGHT$(") || upper.StartsWith("SPACE$(") ||
               upper.StartsWith("STRING$(") || upper.StartsWith("LTRIM$(") ||
               upper.StartsWith("RTRIM$(");
    }

    private (string s, double n) ParseStrNumArgs(string args)
    {
        // Find comma not inside strings/parens
        int depth = 0; bool inStr = false;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == '"') inStr = !inStr;
            if (inStr) continue;
            if (args[i] == '(') depth++;
            else if (args[i] == ')') depth--;
            else if (args[i] == ',' && depth == 0)
                return (EvalStr(args[..i].Trim()), EvalNum(args[(i + 1)..].Trim()));
        }
        return (EvalStr(args), 0);
    }

    private List<string> SplitFuncArgs(string s)
    {
        var result = new List<string>();
        var current = new StringBuilder();
        bool inStr = false;
        int depth = 0;
        foreach (char c in s)
        {
            if (c == '"') inStr = !inStr;
            if (inStr) { current.Append(c); continue; }
            if (c == '(') depth++;
            else if (c == ')') depth--;
            else if (c == ',' && depth == 0)
            {
                result.Add(current.ToString().Trim());
                current.Clear();
                continue;
            }
            current.Append(c);
        }
        result.Add(current.ToString().Trim());
        return result;
    }

    private static int FindPlusOutside(string s)
    {
        bool inStr = false;
        int depth = 0;
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] == '"') inStr = !inStr;
            if (inStr) continue;
            if (s[i] == '(') depth++;
            else if (s[i] == ')') depth--;
            else if (s[i] == '+' && depth == 0) return i;
        }
        return -1;
    }

    private static int FindMatchingParen(string s, int openIdx)
    {
        int depth = 0;
        for (int i = openIdx; i < s.Length; i++)
        {
            if (s[i] == '(') depth++;
            else if (s[i] == ')') { depth--; if (depth == 0) return i; }
        }
        return s.Length - 1;
    }

    private void BuildDataList()
    {
        _dataList.Clear();
        foreach (var (_, body) in _program)
        {
            string upper = body.TrimStart().ToUpperInvariant();
            if (upper.StartsWith("DATA"))
            {
                string rest = body.TrimStart()[4..].Trim();
                foreach (var item in rest.Split(','))
                {
                    if (double.TryParse(item.Trim(), out double d))
                        _dataList.Add(d);
                }
            }
        }
        _dataPtr = 0;
    }

    private static string FormatNumber(double n)
    {
        if (n == Math.Floor(n) && Math.Abs(n) < 1e9)
            return ((long)n).ToString();
        return n.ToString("G", System.Globalization.CultureInfo.InvariantCulture);
    }

    // ── File I/O ──────────────────────────────────────────────────────────────

    private void LoadFile(string filename)
    {
        if (!filename.Contains('.')) filename += ".BAS";
        var (name, ext) = CpmDisk.ParseFilename(filename.ToUpperInvariant());
        var file = _disk!.OpenFile(name, ext);
        if (file == null) { Print($"?File not found: {filename}\r\n"); return; }

        byte[] content = _disk.GetFileBytes(file);
        string text = Encoding.ASCII.GetString(content);
        _program.Clear();
        foreach (var line in text.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n'))
        {
            string l = line.Trim();
            if (l.Length == 0) continue;
            int i = 0;
            while (i < l.Length && char.IsDigit(l[i])) i++;
            if (i > 0 && int.TryParse(l[..i], out int num))
                _program[num] = l[i..].Trim();
        }
        Print($"Ok ({_program.Count} lines)\r\n");
    }

    private void SaveFile(string filename)
    {
        if (!filename.Contains('.')) filename += ".BAS";
        var (name, ext) = CpmDisk.ParseFilename(filename.ToUpperInvariant());
        var file = _disk!.OpenOrCreate(name, ext);

        var sb = new StringBuilder();
        foreach (var (num, body) in _program)
            sb.Append($"{num} {body}\r\n");

        _disk.SetFileBytes(file, Encoding.ASCII.GetBytes(sb.ToString()));
        _disk.FlushFile(file);
        Print($"Ok (saved {_program.Count} lines)\r\n");
    }

    private static string ExtractFilename(string s)
    {
        s = s.Trim();
        if (s.StartsWith('"') && s.EndsWith('"')) s = s[1..^1];
        return s.ToUpperInvariant();
    }

    // ── Runtime helpers ───────────────────────────────────────────────────────

    private void ClearRuntime()
    {
        _numVars.Clear();
        _strVars.Clear();
        _numArrays.Clear();
        _strArrays.Clear();
        _forStack.Clear();
        _gosubStack.Clear();
        _dataPtr = 0;
        _lineIdx = 0;
    }

    private void Print(string s) => _output(s);

    private string ReadLine()
    {
        var sb = new StringBuilder();
        while (!_ct.IsCancellationRequested)
        {
            byte b;
            try { b = _input.BlockingRead(_ct); } catch { break; }

            if (b == 13 || b == 10) { _output("\r\n"); break; }
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
            else if (b == 3) // Ctrl+C
            {
                _output("^C\r\n");
                throw new BasicException("Break");
            }
        }
        return sb.ToString();
    }
}

internal sealed class ForState
{
    public string Var = "";
    public double To;
    public double Step;
    public int BodyIdx;  // _lineIdx of first statement after FOR
}

internal sealed class BasicException : Exception
{
    public BasicException(string msg) : base(msg) { }
}

internal sealed class GotoException : Exception
{
    public int Target { get; }
    public GotoException(int target) : base($"Line {target} not found") { Target = target; }
}
