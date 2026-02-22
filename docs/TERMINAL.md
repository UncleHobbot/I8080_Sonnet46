# CP/M 2.2 Terminal Guide

This emulator runs real CP/M 2.2 machine code on an emulated Intel 8080A processor. The terminal in the browser is a full VT100-compatible xterm connected via WebSocket — everything you type goes directly to the CP/M Console Command Processor (CCP).

---

## The Prompt

```
A>
```

`A` is the current drive. `>` means CP/M is ready for input. You're in the **Transient Program Area** — type a command and press **Enter**.

---

## Built-in CCP Commands

These are built into CP/M itself and require no file on disk.

### DIR — List files

```
A>DIR
A>DIR *.COM
A>DIR B:
```

Lists files on the current (or specified) drive. Wildcards `*` (any chars) and `?` (single char) are supported.

### TYPE — Display a file

```
A>TYPE README.TXT
```

Prints the contents of a text file to the terminal. Binary files will produce garbage output.

### ERA — Erase files

```
A>ERA TEMP.TXT
A>ERA *.BAK
```

Wildcards work. CP/M will ask `ALL (Y/N)?` if you erase more than one file.

### REN — Rename a file

```
A>REN NEWNAME.TXT=OLDNAME.TXT
```

Note the order: **new name = old name**.

### SAVE — Save memory to file

```
A>SAVE 4 OUTPUT.COM
```

Saves *n* 256-byte pages from the TPA (starting at 0x0100) to a file. Rarely used interactively.

### USER — Change user area

```
A>USER 3
```

CP/M supports 16 user areas (0–15) per drive. Files in different user areas are hidden from each other. Default is user 0.

---

## Running Programs

Type the program name (without `.COM`) and press Enter.

```
A>MBASIC
A>ASM HELLO
A>ED HELLO.ASM
```

CP/M searches drive A (user 0) for a matching `.COM` file, loads it at 0x0100, and jumps to it.

---

## Available Programs

### MBASIC — Microsoft BASIC 5.21

```
A>MBASIC
```

Full Microsoft BASIC interpreter with file I/O, string functions, and floating-point math.

**Key commands inside MBASIC:**

| Command | Description |
|---------|-------------|
| `10 PRINT "HELLO"` | Enter a program line |
| `RUN` | Execute the program |
| `LIST` | List the program |
| `LIST 10-50` | List lines 10 through 50 |
| `NEW` | Clear program |
| `SAVE "PROG.BAS"` | Save to disk |
| `LOAD "PROG.BAS"` | Load from disk |
| `SYSTEM` | Exit back to CP/M |

**Quick example:**

```basic
10 FOR I = 1 TO 10
20   PRINT I, I*I
30 NEXT I
40 END
RUN
```

---

### ED — CP/M Line Editor

```
A>ED HELLO.ASM
```

ED is a line-oriented editor — you work with lines, not a full-screen cursor. A new file starts empty. An existing file is loaded for editing.

**ED prompt is `*`.**

| Command | Description |
|---------|-------------|
| `I` | Insert mode — type text, one line per Enter; end with **Ctrl+Z** |
| `#T` | Type (display) all lines |
| `1T` | Display line 1 |
| `T` | Display current line |
| `N` | Advance to next line |
| `#N` | Go to last line |
| `1,5T` | Display lines 1–5 |
| `S/old/new/` | Substitute first occurrence on current line |
| `NS/old/new/` | Substitute on all remaining lines |
| `D` | Delete current line |
| `1,5D` | Delete lines 1–5 |
| `E` | Save and exit |
| `Q` | Quit without saving (asks confirmation) |
| `H` | Rewind to start of file (re-opens for editing) |

**Creating a new file:**

```
A>ED HELLO.ASM
NEW FILE
*I
        ORG     0100H
START:  MVI     A,'H'
        CALL    PUTCH
        HLT
        END     START
^Z
*E
```

---

### ASM — Intel 8080 Assembler

```
A>ASM HELLO
```

Assembles `HELLO.ASM` → produces `HELLO.COM` (and `HELLO.PRN` listing). The source file must have a `.ASM` extension; omit it on the command line.

**Basic 8080 assembly syntax:**

```asm
        ORG     0100H           ; Programs start at 0x0100 in CP/M

BDOS    EQU     0005H           ; BDOS entry point
CONOUT  EQU     2               ; BDOS function: output char in E

        MVI     C, CONOUT       ; Function number
        MVI     E, 'A'          ; Character to print
        CALL    BDOS            ; Call BDOS
        RET                     ; Return to CP/M

        END
```

After assembly, run with:

```
A>HELLO
```

**BDOS functions** (call via `CALL 0005H` with function number in `C`):

| C | Function | Input | Output |
|---|----------|-------|--------|
| 0 | System Reset | — | — |
| 1 | Console Input | — | A = char |
| 2 | Console Output | E = char | — |
| 9 | Print String | DE = addr of `$`-terminated string | — |
| 10 | Read Console Buffer | DE = buffer | — |
| 11 | Console Status | — | A = 0/1 |
| 13 | Reset Disk | — | — |
| 14 | Select Disk | E = drive (0=A) | — |
| 15 | Open File | DE = FCB | A = 0 ok / 255 fail |
| 16 | Close File | DE = FCB | — |
| 17 | Search First | DE = FCB | A = 0 found / 255 not |
| 18 | Search Next | — | A = 0 found / 255 not |
| 19 | Delete File | DE = FCB | — |
| 20 | Sequential Read | DE = FCB | A = 0 ok |
| 21 | Sequential Write | DE = FCB | A = 0 ok |
| 22 | Make File | DE = FCB | A = 0 ok / 255 fail |
| 25 | Return Current Disk | — | A = drive (0=A) |
| 26 | Set DMA Address | DE = addr | — |

---

### PIP — Peripheral Interchange Program

Copy files between drives, devices, and files.

```
A>PIP B:=A:*.COM        ; Copy all .COM files to drive B
A>PIP A:NEW.TXT=OLD.TXT ; Rename via copy
A>PIP CON:=FILE.TXT     ; Print file to terminal
A>PIP FILE.TXT=CON:     ; Capture terminal input to file (end with ^Z)
```

---

### STAT — Disk and File Statistics

```
A>STAT               ; Show disk space on current drive
A>STAT *.*           ; Show file sizes
A>STAT DSK:          ; Show disk parameters
```

---

### DDT — Dynamic Debugging Tool

Low-level debugger for 8080 code.

```
A>DDT HELLO.COM
```

**DDT commands:**

| Command | Description |
|---------|-------------|
| `D` | Display memory (hex + ASCII dump) |
| `D 0100` | Dump from address 0100h |
| `L` | List (disassemble) instructions |
| `L 0100` | Disassemble from 0100h |
| `G` | Go (run from current PC) |
| `G 0100` | Run from address 0100h |
| `T` | Trace one instruction |
| `T 5` | Trace 5 instructions |
| `S 0100` | Set/examine memory at 0100h |
| `X` | Examine/modify registers |
| `^C` | Exit DDT back to CP/M |

---

### DUMP — Hex dump a file

```
A>DUMP FILE.COM
```

Displays the raw bytes of a file in hex + ASCII, 16 bytes per line.

---

## Special Keys

| Key | Effect |
|-----|--------|
| **Enter** | Send command / end input line |
| **Ctrl+C** | Abort current program, return to CP/M |
| **Ctrl+Z** | End-of-file marker (exit insert mode in ED, end PIP input) |
| **Ctrl+S** | Pause output (scroll stop) |
| **Ctrl+Q** | Resume output |
| **Ctrl+P** | Toggle printer echo (no real printer, but captured in terminal) |
| **Backspace / Del** | Erase last character |
| **Ctrl+U** | Cancel current input line |
| **Ctrl+R** | Retype current line (after edits) |
| **Ctrl+X** | Erase entire input line |
| **Ctrl+E** | Physical end of line (within long input) |

---

## Drives

CP/M supports drives A through D. Drive A is mounted by default with the system disk.

Switch drives by typing the drive letter followed by a colon:

```
A>B:
B>A:
A>
```

The debug panel on the right shows mounted disk images and allows uploading/downloading `.dsk` files.

---

## Hello World Walkthrough

A complete example: write, assemble, and run a "Hello, World!" program.

**1. Create the source file:**

```
A>ED HELLO.ASM
NEW FILE
*I
BDOS    EQU     0005H
CONOUT  EQU     2

        ORG     0100H
START:  MVI     C, CONOUT
        MVI     E, 'H'
        CALL    BDOS
        MVI     E, 'I'
        CALL    BDOS
        MVI     E, 0DH
        CALL    BDOS
        RET
        END     START
^Z
*E
```

**2. Assemble:**

```
A>ASM HELLO
```

**3. Run:**

```
A>HELLO
HI
A>
```

---

## Memory Map Reference

```
0x0000–0x00FF   Zero page (JMP WBOOT at 0x0000, JMP BDOS at 0x0005)
0x0100–0xCFFF   TPA — your programs load and run here
0xE400–0xEBFF   CCP (Console Command Processor)
0xEC00–0xF9FF   BDOS (Basic Disk Operating System)
0xFA00–0xFFFF   BIOS (I/O driver, jump table)
```

The **Memory panel** in the debug sidebar lets you inspect any address range in real time. The **Registers panel** shows A, BC, DE, HL, SP, PC, and all flags live while the CPU runs.
