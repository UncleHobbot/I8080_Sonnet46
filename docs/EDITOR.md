# EDIT — Screen Text Editor

The built-in `EDIT` command is a full-screen VT100 text editor for creating and modifying source files on disk.

## Launching

```
A>EDIT HELLO.ASM       ; open or create a file
A>EDIT NOTES.TXT
A>EDIT                 ; open an empty scratch buffer
```

The editor uses an alternate screen buffer — your terminal history is preserved and restored on exit.

---

## Screen Layout

```
┌─────────────────────────────────────────────────────────────────────────┐
│ EDIT: HELLO.ASM  Line 1/24  Col 1                         [modified]    │  ← title bar
├─────────────────────────────────────────────────────────────────────────┤
│ BDOS    EQU     0005H                                                   │
│         ORG     0100H                                                   │
│ START:  MVI     C,2                                                     │
│         MVI     E,'H'                                                   │
│         CALL    BDOS                                                    │
│         RET                                                             │
│         END                                                             │
│                                                                         │
│                                                                         │
│  (22 lines of text)                                                     │
│                                                                         │
├─────────────────────────────────────────────────────────────────────────┤
│ ^S Save  ^Q Quit  ^K Del Line  ^F Find  ^G Goto  ^Y Del EOL            │  ← status bar
└─────────────────────────────────────────────────────────────────────────┘
```

- **Title bar** (row 1): filename, cursor position, modified indicator
- **Edit area** (rows 2–23): 22 lines visible at once; scrolls vertically and horizontally
- **Status bar** (row 24): key reference + transient messages

---

## Key Bindings

### Navigation

| Key | Action |
|-----|--------|
| Arrow keys | Move cursor one step |
| **Home** | Beginning of line |
| **End** | End of line |
| **Ctrl+Home** | First line of file |
| **Ctrl+End** | Last line of file |
| **PgUp** | Scroll up one screen |
| **PgDn** | Scroll down one screen |

### Editing

| Key | Action |
|-----|--------|
| Any printable key | Insert character at cursor |
| **Enter** | Insert new line; cursor moves to start of new line |
| **Backspace** | Delete character to the left |
| **Delete** | Delete character under cursor |
| **Ctrl+K** | Delete the entire current line |
| **Ctrl+Y** | Delete from cursor to end of line |

### File Operations

| Key | Action |
|-----|--------|
| **Ctrl+S** or **F2** | Save file to disk |
| **Ctrl+Q** or **Ctrl+X** | Quit (prompts if unsaved changes) |

### Search and Navigation

| Key | Action |
|-----|--------|
| **Ctrl+F** | Find text — prompts for a search string, jumps to first match |
| **Ctrl+G** | Go to line — prompts for a line number |

---

## Saving

Press **Ctrl+S** at any time to save the current buffer to disk. The status bar confirms:

```
Saved HELLO.ASM
```

If no filename was given (opened with `EDIT` and no argument), you will be prompted for a filename.

---

## Quitting

Press **Ctrl+Q** or **Ctrl+X** to quit.

- If there are unsaved changes, the editor asks:
  ```
  Unsaved changes. Save before quit? (Y/N/C)
  ```
  - **Y** — save and exit
  - **N** — discard changes and exit
  - **C** — cancel, return to editing

---

## Example: Writing a Hello World Program

```
A>EDIT HELLO.ASM
```

The editor opens an empty buffer. Type your program:

```
BDOS    EQU     0005H
CONOUT  EQU     2

        ORG     0100H

START:  MVI     C, CONOUT
        MVI     E, 'H'
        CALL    BDOS
        MVI     E, 'E'
        CALL    BDOS
        MVI     E, 'L'
        CALL    BDOS
        MVI     E, 'L'
        CALL    BDOS
        MVI     E, 'O'
        CALL    BDOS
        MVI     E, 0DH
        CALL    BDOS
        MVI     E, 0AH
        CALL    BDOS
        RET

        END     START
```

Press **Ctrl+S** to save, then **Ctrl+Q** to quit.

Assemble and run:
```
A>ASM HELLO
A>HELLO
HELLO
A>
```

---

## Notes

- Maximum line length: 255 characters (long lines scroll horizontally)
- The editor handles ASCII text only; binary files may display incorrectly
- Files are stored in CP/M text format (lines end with CR+LF; file ends with Ctrl+Z `0x1A`)
- The file extension defaults to `.TXT` if you omit it; include `.ASM` for assembly sources
