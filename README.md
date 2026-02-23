# Intel 8080 / CP/M 2.2 Emulator

A browser-accessible emulator of an Intel 8080 personal computer running CP/M 2.2. Everything — CPU, BIOS, BDOS, CCP, text editor, assembler, and BASIC interpreter — is implemented in .NET. **No external ROM binaries are required.**

## Features

- **Cycle-accurate Intel 8080A CPU** — all ~246 opcodes, correct flags, configurable speed (100 KHz – 10 MHz)
- **Complete CP/M 2.2** — BIOS, BDOS, and CCP implemented in .NET; no external binary needed
- **Built-in screen editor** (`EDIT`) — full-screen VT100 editor with arrow keys, find, goto
- **Built-in assembler** (`ASM`) — two-pass Intel 8080 assembler; reads `.ASM`, writes `.COM`
- **Built-in BASIC** (`BASIC`) — line-numbered BASIC with FOR/NEXT, GOSUB, file I/O, math functions
- **Disk image support** — upload/download standard 8" CP/M `.dsk` files (256 KB each, up to 4 drives)
- **Debug panel** — live register view, memory hex dump, step/pause/reset, speed control

## Architecture

| Layer | Implementation |
|-------|---------------|
| CPU | `src/Emulator/CPU/I8080.cs` — cycle-accurate, all opcodes |
| BIOS | `src/Emulator/Bios/BiosHandler.cs` — OUT-instruction trap at ports 0–16 |
| BDOS | `src/Emulator/Cpm/BdosHandler.cs` — OUT trap at port 17 (address 0x0005) |
| CCP  | `src/Emulator/Cpm/CcpHandler.cs` — runs on CPU thread, blocks on terminal input |
| Editor | `src/Emulator/Programs/TextEditor.cs` — ANSI/VT100 screen editor |
| Assembler | `src/Emulator/Programs/Assembler8080.cs` — two-pass 8080 assembler |
| BASIC | `src/Emulator/Programs/BasicInterpreter.cs` — full line-numbered interpreter |
| Server | `src/Server/` — ASP.NET Core 10 + SignalR hub |
| Frontend | `src/Frontend/` — React + Vite + TypeScript + xterm.js |

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [Node.js 20+](https://nodejs.org/)

No CP/M ROMs, no external `.COM` files, no additional downloads.

## Quick Start

### 1. Start the backend

```bash
dotnet run --project src/Server --urls "http://localhost:5000"
```

The server starts on `http://localhost:5000`. CP/M boots automatically.

### 2. Start the frontend (development)

```bash
cd src/Frontend
npm install
npm run dev
```

Navigate to `http://localhost:5173`. The `A>` prompt appears within a second.

### 3. Production build

```bash
cd src/Frontend && npm run build  # outputs to src/Server/wwwroot
dotnet run --project src/Server   # serves everything at :5000
```

## Using the Emulator

Once booted, you'll see the CP/M prompt:

```
A>
```

### Basic commands

```
A>DIR               List files on current drive
A>EDIT HELLO.ASM    Create/edit a text file
A>ASM HELLO         Assemble HELLO.ASM → HELLO.COM
A>HELLO             Run HELLO.COM
A>BASIC             Start the BASIC interpreter
A>TYPE FILE.TXT     Display a file
A>ERA *.BAK         Delete files
A>REN NEW=OLD       Rename a file
A>B:                Switch to drive B
```

### Writing and running a program

```
A>EDIT HELLO.ASM
```
*Type your assembly program, Ctrl+S to save, Ctrl+Q to quit*

```
A>ASM HELLO
Assembling HELLO.ASM...
HELLO.ASM: 0 error(s)
Written HELLO.COM (17 bytes)

A>HELLO
Hello, World!
A>
```

## Documentation

| Guide | Contents |
|-------|----------|
| [docs/TERMINAL.md](docs/TERMINAL.md) | CP/M commands, drives, memory map, keyboard shortcuts |
| [docs/EDITOR.md](docs/EDITOR.md) | EDIT screen editor — keys, navigation, search |
| [docs/ASSEMBLER.md](docs/ASSEMBLER.md) | ASM assembler — all mnemonics, directives, BDOS interface |
| [docs/BASIC.md](docs/BASIC.md) | BASIC interpreter — statements, functions, examples |

## Running Tests

```bash
dotnet test tests/Emulator.Tests/      # 36 CPU opcode unit tests
dotnet test tests/Integration.Tests/  # 2 headless boot integration tests
```

All 38 tests pass with no external files required.

## Disk Images

Drive A starts with a blank formatted disk. You can:

- **Upload** a `.dsk` file via the Disk panel in the browser to mount it on any drive (A–D)
- **Download** the current disk image to save your work
- **Build** a populated disk image from `.COM` files in `roms/`:

```bash
dotnet run --project tools/MkDisk
```

This creates `disks/cpm22_system.dsk`. Place any standard CP/M `.COM` files in `roms/` before running MkDisk.

Standard disk format: 77 tracks × 26 sectors × 128 bytes = **256,256 bytes**.

## Debug Panel

The right-side panel provides:

- **Control**: Run / Pause / Step / Reset / Hard Reset, CPU speed slider
- **Registers**: A, BC, DE, HL, SP, PC, and all flags live while running
- **Memory**: Hex dump with address navigation (auto-follows PC)
- **Disks**: Upload/download `.dsk` files, see disk activity indicators

## Project Structure

```
i8080_Sonnet46/
├── src/
│   ├── Emulator/          # CPU, memory, BIOS, BDOS, CCP, programs (library)
│   │   ├── CPU/           # I8080.cs, Instructions.cs, Flags.cs
│   │   ├── Memory/        # MemoryBus.cs, MemoryMap.cs
│   │   ├── Bios/          # BiosHandler.cs, TerminalInputQueue.cs
│   │   ├── Cpm/           # CpmSystem.cs, BdosHandler.cs, CcpHandler.cs, CpmDisk.cs
│   │   ├── Disk/          # DiskSystem.cs, DiskImage.cs
│   │   ├── IO/            # IoPort.cs
│   │   └── Programs/      # TextEditor.cs, Assembler8080.cs, BasicInterpreter.cs
│   ├── Server/            # ASP.NET Core + SignalR hub + EmulatorService
│   └── Frontend/          # React + xterm.js terminal UI
├── tests/
│   ├── Emulator.Tests/    # CPU opcode unit tests (no ROM required)
│   └── Integration.Tests/ # Headless boot tests (pure .NET, no ROM required)
├── tools/
│   └── MkDisk/            # Builds cpm22_system.dsk from roms/*.COM
├── docs/
│   ├── TERMINAL.md        # CP/M terminal user guide
│   ├── EDITOR.md          # EDIT screen editor guide
│   ├── ASSEMBLER.md       # ASM assembler reference
│   └── BASIC.md           # BASIC interpreter reference
├── disks/                 # Generated .dsk files (git-ignored)
└── roms/                  # Optional .COM files for disk population (git-ignored)
```

## Technical Notes

### BIOS Trap Mechanism

BIOS functions are implemented via `OUT`-instruction traps:
- **BIOS jump table** at `0xFA00`: 17 × `JMP stubN` entries
- **BIOS stubs** at `0xFA33`: 17 × `OUT fnIndex, A` + `RET`
- `IoPort.Out()` intercepts ports 0–16 and calls `BiosHandler.Execute(BiosFunction, cpu)`

### BDOS Trap

Instead of a JMP to a binary BDOS, address `0x0005` contains `OUT 17, A` + `RET`. When a program does `CALL 5`, `IoPort.Out()` intercepts port 17 and calls `BdosHandler.Execute(cpu)`, which dispatches on `cpu.C` (the function number).

### CCP Threading Model

The CCP runs on the CPU thread. `BiosHandler.Boot()` calls `CcpHandler.Run()`, which blocks on `TerminalInputQueue.BlockingRead()` while waiting for user input. When the user runs a `.COM` file, `CcpHandler` sets `cpu.PC = 0x0100` and returns. The CPU executor then resumes from the loaded program.

### CP/M Memory Map

```
0x0000–0x00FF   Zero page (WBOOT vector, BDOS trap, drive number)
0x0100–0xCFFF   TPA — programs load here
0xE400–0xEBFF   CCP area
0xEC00–0xF9FF   BDOS area
0xFA00–0xFFFF   BIOS jump table, stubs, and DPH/DPB structures
```
