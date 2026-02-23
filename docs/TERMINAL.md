# CP/M 2.2 Terminal Guide

This emulator runs a complete Intel 8080 personal computer with CP/M 2.2 entirely in .NET — no external ROMs or binaries required. The terminal in the browser is a full VT100-compatible xterm connected via WebSocket.

---

## The Prompt

```
A>
```

`A` is the current drive. `>` means CP/M is ready for input. Type a command and press **Enter**.

---

## Built-in CCP Commands

These commands are built into the CP/M Console Command Processor and require no file on disk.

### DIR — List files

```
A>DIR
A>DIR *.COM
A>DIR *.ASM
A>DIR B:
```

Lists files on the current (or specified) drive. Wildcards:
- `*` — matches any sequence of characters
- `?` — matches any single character

### TYPE — Display a file

```
A>TYPE README.TXT
A>TYPE HELLO.ASM
```

Prints the contents of a text file to the terminal. Lines ending with `Ctrl+Z` (0x1A) mark the CP/M end-of-file.

### ERA / DEL — Erase files

```
A>ERA TEMP.TXT
A>ERA *.BAK
A>DEL *.TMP
```

Wildcards are supported. If the pattern matches multiple files, CP/M asks `Delete all? (Y/N)`.

### REN — Rename a file

```
A>REN NEWNAME.TXT=OLDNAME.TXT
```

**Order: new name = old name.** Both files must be on the same drive.

### SAVE — Save memory to file

```
A>SAVE 4 OUTPUT.COM
```

Saves *n* 256-byte pages of memory starting at address 0x0100 (the TPA) to a file. Useful for capturing assembled output or memory snapshots.

### USER — Change user area

```
A>USER 3
A>USER 0
```

CP/M supports 16 user areas (0–15) per drive. Files in different user areas are hidden from each other. Default is user 0.

### CLS — Clear screen

```
A>CLS
```

### VER — Version

```
A>VER
CP/M 2.2 (.NET)
```

### HELP — Show available commands

```
A>HELP
```

Lists all built-in commands and built-in programs.

---

## Built-in Programs

These programs are implemented in .NET and are always available — no `.COM` files needed on disk.

### EDIT — Screen text editor

```
A>EDIT HELLO.ASM
A>EDIT NOTES.TXT
A>EDIT
```

Full-screen VT100 editor with arrow-key navigation, find/replace, and direct disk save.
See [EDITOR.md](EDITOR.md) for complete documentation.

### ASM — Intel 8080 assembler

```
A>ASM HELLO
```

Two-pass assembler: reads `HELLO.ASM`, writes `HELLO.COM`.
See [ASSEMBLER.md](ASSEMBLER.md) for complete documentation.

### BASIC — BASIC interpreter

```
A>BASIC
A>BASIC GAME.BAS
```

Line-numbered BASIC with full arithmetic, string handling, file I/O, and FOR/NEXT loops.
See [BASIC.md](BASIC.md) for complete documentation.

---

## Drives

CP/M supports drives A through D. Switch by typing the drive letter and colon:

```
A>B:
B>A:
A>
```

Drive A is automatically mounted at startup. Additional drives can be mounted via the **Disk panel** in the debug sidebar (upload a `.dsk` file).

---

## Running .COM Programs

If a `.COM` file exists on the current drive (or drive A), type its name without the extension:

```
A>HELLO
```

CP/M loads the file at address 0x0100 and runs it. Press **Ctrl+C** to abort a running program and return to the prompt.

---

## Special Keys

| Key | Effect |
|-----|--------|
| **Enter** | Send command |
| **Backspace** | Delete last character |
| **Ctrl+C** | Abort current program / cancel input line |
| **Ctrl+U** | Cancel (erase) current input line |
| **Ctrl+Z** | End-of-file (exits insert mode in editors, terminates PIP input) |
| **Ctrl+S** | Pause output |
| **Ctrl+Q** | Resume output |

---

## Hello World Walkthrough

A complete example: write, assemble, and run a program from scratch.

**1. Create the source:**

```
A>EDIT HELLO.ASM
```

Type:

```
BDOS    EQU     0005H
PRTSTR  EQU     9

        ORG     0100H

START:  MVI     C, PRTSTR
        LXI     D, MSG
        CALL    BDOS
        RET

MSG:    DB      'Hello, World!', 0DH, 0AH, '$'
        END     START
```

Press **Ctrl+S** to save, **Ctrl+Q** to quit.

**2. Assemble:**

```
A>ASM HELLO
Assembling HELLO.ASM...
HELLO.ASM: 0 error(s)
Written HELLO.COM (17 bytes)
```

**3. Run:**

```
A>HELLO
Hello, World!
A>
```

---

## Memory Map

```
0x0000–0x00FF   Zero page
                  0x0000: JMP WBOOT
                  0x0004: current drive number
                  0x0005: BDOS entry (OUT 17 trap)
0x0100–0xCFFF   TPA — programs load and run here
0xE400–0xEBFF   CCP area (managed by .NET CcpHandler)
0xEC00–0xF9FF   BDOS area (managed by .NET BdosHandler)
0xFA00–0xFA32   BIOS jump table (17 × JMP)
0xFA33–0xFA82   BIOS stubs (17 × OUT fn + RET)
0xFB00+         DPH/DPB disk parameter blocks
```

The **Memory panel** in the debug sidebar lets you inspect any address range in real time.

---

## Disk Format

Standard 8-inch single-density CP/M 2.2:

```
77 tracks × 26 sectors × 128 bytes/sector = 256,256 bytes
Block size: 1 KB (8 sectors)
Directory: 64 entries (2 blocks)
System tracks: 2 (reserved)
```

`.dsk` files can be uploaded and downloaded via the **Disk panel**.
