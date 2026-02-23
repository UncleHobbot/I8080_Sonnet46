# BASIC — Built-in BASIC Interpreter

The built-in `BASIC` command provides a classic line-numbered BASIC programming environment inspired by early microcomputer BASICs.

## Launching

```
A>BASIC              ; start with an empty program
A>BASIC GAME.BAS     ; load and run a saved program
```

The BASIC prompt is `Ok`. Type `SYSTEM` or press **Ctrl+C** to return to CP/M.

---

## Program Entry

Lines beginning with a number are stored as program lines:

```basic
10 PRINT "Hello, World!"
20 FOR I = 1 TO 5
30   PRINT I, I*I
40 NEXT I
50 END
```

- Line numbers must be 1–65535
- Lines are stored in sorted order regardless of entry order
- Re-entering a line number replaces the previous line
- To delete a line, enter its number with no statement: `20`

---

## Immediate Commands

Commands without a line number execute immediately:

| Command | Description |
|---------|-------------|
| `LIST` | List the entire program |
| `LIST 10-50` | List lines 10 through 50 |
| `RUN` | Run the program from the first line |
| `RUN 50` | Run starting at line 50 |
| `NEW` | Clear the program and all variables |
| `LOAD "FILE.BAS"` | Load a program from disk |
| `SAVE "FILE.BAS"` | Save the program to disk |
| `CONT` | Continue after a `STOP` or **Ctrl+C** |
| `RENUM` | Renumber lines in steps of 10 |
| `AUTO` | Auto-increment line numbers as you type |
| `SYSTEM` | Exit BASIC, return to CP/M |
| `CLS` | Clear the screen |

---

## Statements

### Output

```basic
PRINT "Hello"               ' print string
PRINT A, B, C               ' print multiple values (tab-separated)
PRINT A; B; C               ' print without spaces
PRINT                       ' blank line
```

### Variables and Assignment

Variables are single letters A–Z for numbers, A$–Z$ for strings.

```basic
LET A = 42
LET A$ = "hello"
A = A + 1                   ' LET is optional
```

### Input

```basic
INPUT A                     ' prompt "? " and read a number
INPUT "Name"; N$            ' custom prompt
INPUT A, B, C               ' read multiple values
```

### Conditionals

```basic
IF A > 10 THEN PRINT "big"
IF A > 10 THEN PRINT "big" ELSE PRINT "small"
IF A = 0 THEN GOTO 100
```

### Loops

```basic
FOR I = 1 TO 10
  PRINT I
NEXT I

FOR I = 10 TO 1 STEP -1
  PRINT I
NEXT I
```

### Branching

```basic
GOTO 100
GOSUB 500          ' call subroutine at line 500
RETURN             ' return from subroutine

ON X GOTO 100, 200, 300     ' jump to line based on X (1,2,3)
ON X GOSUB 100, 200, 300    ' call subroutine based on X
```

### Data Statements

```basic
DATA 1, 2, 3, "hello", 4.5
READ A, B, C, D$, E
RESTORE                     ' reset DATA pointer to start
```

### Arrays

```basic
DIM A(100)                  ' numeric array of 100 elements (1-indexed)
DIM B$(20)                  ' string array of 20 elements
A(1) = 42
PRINT A(1)
```

### Miscellaneous

```basic
REM this is a comment
STOP                        ' pause execution (CONT resumes)
END                         ' end program
CLS                         ' clear screen
SLEEP 1000                  ' pause for 1000 milliseconds
POKE addr, value            ' write byte to memory (decimal address)
```

---

## Operators

### Arithmetic (highest to lowest precedence)

| Operator | Description |
|----------|-------------|
| `^` | Exponentiation |
| unary `-` | Negation |
| `*`, `/` | Multiply, divide |
| `+`, `-` | Add, subtract |

### Relational (produce 1 for true, 0 for false)

| Operator | Description |
|----------|-------------|
| `=` | Equal |
| `<>` | Not equal |
| `<`, `>` | Less than, greater than |
| `<=`, `>=` | Less/greater or equal |

### Logical

| Operator | Description |
|----------|-------------|
| `NOT` | Logical NOT |
| `AND` | Logical AND |
| `OR` | Logical OR |

---

## Numeric Functions

| Function | Description |
|----------|-------------|
| `ABS(x)` | Absolute value |
| `INT(x)` | Integer part (floor) |
| `SGN(x)` | Sign: -1, 0, or 1 |
| `SQR(x)` | Square root |
| `SIN(x)` | Sine (radians) |
| `COS(x)` | Cosine (radians) |
| `TAN(x)` | Tangent (radians) |
| `ATN(x)` | Arctangent (radians) |
| `EXP(x)` | e^x |
| `LOG(x)` | Natural logarithm |
| `RND(x)` | Random number 0–1 (x ignored) |
| `FRE(x)` | Returns 0 (placeholder) |

---

## String Functions

| Function | Description |
|----------|-------------|
| `LEN(a$)` | Length of string |
| `LEFT$(a$, n)` | First n characters |
| `RIGHT$(a$, n)` | Last n characters |
| `MID$(a$, start, len)` | Substring (1-indexed) |
| `CHR$(n)` | Character with ASCII code n |
| `ASC(a$)` | ASCII code of first character |
| `STR$(x)` | Convert number to string |
| `VAL(a$)` | Convert string to number |
| `SPACE$(n)` | String of n spaces |
| `STRING$(n, c$)` | String of n copies of c$ |
| `LTRIM$(a$)` | Remove leading spaces |
| `RTRIM$(a$)` | Remove trailing spaces |

---

## Example Programs

### Fibonacci Sequence

```basic
10 A = 0 : B = 1
20 FOR I = 1 TO 20
30   PRINT A;
40   C = A + B : A = B : B = C
50 NEXT I
60 PRINT
70 END
```

### Prime Number Sieve

```basic
10 DIM P(100)
20 FOR I = 2 TO 100 : P(I) = 1 : NEXT I
30 FOR I = 2 TO 10
40   IF P(I) = 0 THEN 70
50   FOR J = I*I TO 100 STEP I
60     P(J) = 0
70   NEXT J
80 NEXT I
90 FOR I = 2 TO 100
100  IF P(I) = 1 THEN PRINT I;
110 NEXT I
120 PRINT
130 END
```

### Guessing Game

```basic
10  REM Number guessing game
20  LET SECRET = INT(RND(1) * 100) + 1
30  PRINT "Guess a number between 1 and 100"
40  INPUT "Your guess"; G
50  IF G = SECRET THEN PRINT "Correct!" : END
60  IF G < SECRET THEN PRINT "Too low!"
70  IF G > SECRET THEN PRINT "Too high!"
80  GOTO 40
```

### Save and Load

```basic
RUN

' ... run your program, then save it:
SAVE "GAME.BAS"

' Later, load it back:
LOAD "GAME.BAS"
RUN
```

---

## Notes

- All numeric variables are double-precision floating point
- String variables hold up to 255 characters
- Array indices are 1-based (element 0 exists but is unused by convention)
- Division by zero prints `?Division by zero` and stops execution
- **Ctrl+C** during `RUN` breaks execution; `CONT` resumes from the breakpoint
- Files are saved as plain text (one `line_number statement` per line)
