# ASM — Intel 8080 Assembler

The built-in `ASM` command is a two-pass Intel 8080 assembler. It reads a `.ASM` source file from the current drive and produces a `.COM` executable.

## Usage

```
A>ASM HELLO         ; assembles HELLO.ASM → HELLO.COM
A>ASM B:MYPROG      ; assembles from drive B
```

Omit the `.ASM` extension on the command line. The assembler always reads `FILENAME.ASM` and writes `FILENAME.COM` on the same drive.

---

## Source File Format

Each line has the form:

```
[LABEL:]   [MNEMONIC   [OPERAND[,OPERAND]]]   [; comment]
```

- **Labels** must start in column 1 and end with a colon (the colon is optional in label definitions but required in label references for clarity)
- **Mnemonics and directives** are case-insensitive
- **Comments** start with `;` and extend to end of line
- Blank lines are ignored

---

## Directives

| Directive | Syntax | Description |
|-----------|--------|-------------|
| `ORG` | `ORG 0100H` | Set the assembly origin (default `0x0100`) |
| `EQU` | `NAME EQU value` | Define a constant symbol |
| `DB` | `DB 0FFH, 'A', "hello"` | Emit one or more bytes / ASCII string |
| `DW` | `DW 1234H, LABEL` | Emit 16-bit word(s), little-endian |
| `DS` | `DS 16` | Reserve *n* bytes of space (filled with 0) |
| `END` | `END [startlabel]` | Mark end of source |

---

## Number Formats

| Format | Example | Notes |
|--------|---------|-------|
| Decimal | `123` | Digits 0–9 |
| Hexadecimal | `0FFH` or `0xFF` | Must start with a digit when using `H` suffix |
| Binary | `01110001B` | Digits 0–1, ends with `B` |
| Character | `'A'` | Single ASCII character |
| String | `"hello"` | Only valid inside `DB` |

**Arithmetic expressions** are supported in operands:

```asm
BDOS     EQU   5
MAXBUF   EQU   128
BUFEND   EQU   BUF + MAXBUF - 1
```

Operators: `+`, `-`, `*`, `/`, `%` (modulo), `&` (AND), `|` (OR), `^` (XOR)

---

## Register Names

| 8-bit | 16-bit pairs |
|-------|-------------|
| `A`, `B`, `C`, `D`, `E`, `H`, `L`, `M` | `B` (BC), `D` (DE), `H` (HL), `SP`, `PSW` |

`M` means "memory at address HL" in 8-bit register context.

---

## Instruction Reference

### Data Transfer

| Mnemonic | Description |
|----------|-------------|
| `MOV r1,r2` | Copy register r2 to r1 |
| `MVI r,byte` | Load immediate byte into register |
| `LXI rp,word` | Load 16-bit immediate into register pair |
| `LDA addr` | Load A from memory address |
| `STA addr` | Store A to memory address |
| `LHLD addr` | Load HL from memory |
| `SHLD addr` | Store HL to memory |
| `LDAX rp` | Load A from address in BC or DE |
| `STAX rp` | Store A to address in BC or DE |
| `XCHG` | Exchange DE and HL |

### Arithmetic

| Mnemonic | Description |
|----------|-------------|
| `ADD r` | Add register to A |
| `ADI byte` | Add immediate byte to A |
| `ADC r` | Add register + carry to A |
| `ACI byte` | Add immediate + carry to A |
| `SUB r` | Subtract register from A |
| `SUI byte` | Subtract immediate from A |
| `SBB r` | Subtract register + borrow from A |
| `SBI byte` | Subtract immediate + borrow |
| `INR r` | Increment register (does not affect CY) |
| `DCR r` | Decrement register (does not affect CY) |
| `INX rp` | Increment 16-bit register pair |
| `DCX rp` | Decrement 16-bit register pair |
| `DAD rp` | Add 16-bit pair to HL (sets CY) |
| `DAA` | Decimal adjust accumulator |

### Logical

| Mnemonic | Description |
|----------|-------------|
| `ANA r` | AND register with A |
| `ANI byte` | AND immediate with A |
| `ORA r` | OR register with A |
| `ORI byte` | OR immediate with A |
| `XRA r` | XOR register with A |
| `XRI byte` | XOR immediate with A |
| `CMA` | Complement A |
| `CMC` | Complement carry flag |
| `STC` | Set carry flag |
| `CMP r` | Compare register with A (set flags, no result) |
| `CPI byte` | Compare immediate with A |

### Rotate

| Mnemonic | Description |
|----------|-------------|
| `RLC` | Rotate A left through CY |
| `RRC` | Rotate A right through CY |
| `RAL` | Rotate A left through carry |
| `RAR` | Rotate A right through carry |

### Branching

| Mnemonic | Condition | Description |
|----------|-----------|-------------|
| `JMP addr` | always | Unconditional jump |
| `JC addr` | CY=1 | Jump if carry |
| `JNC addr` | CY=0 | Jump if no carry |
| `JZ addr` | Z=1 | Jump if zero |
| `JNZ addr` | Z=0 | Jump if not zero |
| `JM addr` | S=1 | Jump if minus |
| `JP addr` | S=0 | Jump if plus |
| `JPE addr` | P=1 | Jump if parity even |
| `JPO addr` | P=0 | Jump if parity odd |
| `CALL addr` | always | Call subroutine |
| `CC/CNC/CZ/CNZ/CM/CP/CPE/CPO addr` | conditional | Conditional call |
| `RET` | always | Return from subroutine |
| `RC/RNC/RZ/RNZ/RM/RP/RPE/RPO` | conditional | Conditional return |
| `RST n` | — | Restart (call 0x0000+8n), n=0–7 |
| `PCHL` | — | Jump to address in HL |

### Stack

| Mnemonic | Description |
|----------|-------------|
| `PUSH rp` | Push 16-bit pair onto stack (pairs: B, D, H, PSW) |
| `POP rp` | Pop 16-bit pair from stack |
| `XTHL` | Exchange top of stack with HL |
| `SPHL` | Copy HL to SP |

### I/O and Control

| Mnemonic | Description |
|----------|-------------|
| `IN port` | Read I/O port into A |
| `OUT port` | Write A to I/O port |
| `EI` | Enable interrupts |
| `DI` | Disable interrupts |
| `HLT` | Halt CPU |
| `NOP` | No operation |

---

## CP/M BDOS Interface

CP/M programs call the BDOS via `CALL 0005H` with the function number in register `C` and parameters in `DE`:

```asm
BDOS    EQU     0005H

; Print a character (function 2)
        MVI     C, 2            ; function: CONOUT
        MVI     E, 'A'          ; character to print
        CALL    BDOS

; Print a string (function 9) — string must end with '$'
MSG:    DB      'Hello, World!', 0DH, 0AH, '$'
        MVI     C, 9            ; function: PRINT STRING
        LXI     D, MSG          ; DE = address of string
        CALL    BDOS

; Read a character (function 1)
        MVI     C, 1            ; function: CONIN
        CALL    BDOS            ; A = character read
```

### Common BDOS Functions

| C | Function | DE input | A return |
|---|----------|----------|----------|
| 0 | System Reset | — | — |
| 1 | Console Input | — | char |
| 2 | Console Output | E=char | — |
| 6 | Direct I/O | E=0xFF (read) / char (write) | char or — |
| 9 | Print String | addr of `$`-terminated string | — |
| 10 | Read Console Buffer | addr of buffer (max,count,data) | — |
| 11 | Console Status | — | 0=no, 1=yes |
| 13 | Reset Disk | — | — |
| 14 | Select Disk | E=drive (0=A,1=B,...) | — |
| 15 | Open File | FCB address | 0=ok, 255=fail |
| 16 | Close File | FCB address | — |
| 17 | Search First | FCB with wildcard | 0=found, 255=not |
| 18 | Search Next | — | 0=found, 255=not |
| 19 | Delete File | FCB address | — |
| 20 | Sequential Read | FCB address | 0=ok |
| 21 | Sequential Write | FCB address | 0=ok |
| 22 | Make File | FCB address | 0=ok, 255=fail |
| 25 | Current Disk | — | drive (0=A) |
| 26 | Set DMA Address | addr | — |

---

## Complete Hello World Example

```asm
; HELLO.ASM — Print "Hello, World!" and return to CP/M

BDOS    EQU     0005H   ; BDOS entry point
CONOUT  EQU     2       ; Console output function
PRTSTR  EQU     9       ; Print string function

        ORG     0100H   ; CP/M programs start here

START:
        MVI     C, PRTSTR
        LXI     D, MSG
        CALL    BDOS
        RET             ; Return to CP/M (same as JMP 0)

MSG:    DB      'Hello, World!', 0DH, 0AH, '$'

        END     START
```

Assemble and run:

```
A>ASM HELLO
Assembling HELLO.ASM...
HELLO.ASM: 0 error(s)
Written HELLO.COM (17 bytes)

A>HELLO
Hello, World!
A>
```

---

## Assembly Output

The assembler prints a summary line after assembly:

```
HELLO.ASM: 0 error(s)
Written HELLO.COM (17 bytes)
```

If there are errors:

```
HELLO.ASM:5: undefined symbol: BDOSS
HELLO.ASM: 1 error(s)
Assembly failed.
```

Errors include the source file name and line number.

---

## Tips

- Programs must start at `0x0100` (the TPA) — always use `ORG 0100H`
- Return to CP/M with `RET` (the zero-page at 0x0000 contains `JMP WBOOT`) or `JMP 0`
- String constants in `DB` use double quotes: `DB "hello", 0DH, 0AH, '$'`
- Character literals use single quotes: `MVI A, 'A'`
- Hex constants must begin with a digit: `0FFH` not `FFH`
