# Intel 8080 / CP/M 2.2 Emulator

A browser-accessible emulator of an Intel 8080 personal computer running CP/M 2.2, with embedded text editor, assembler, and BASIC interpreter.

## Architecture

- **Backend**: .NET 10 (ASP.NET Core + SignalR)
- **Frontend**: React + Vite + TypeScript + xterm.js
- **CPU**: Cycle-accurate Intel 8080A emulation (all ~246 opcodes)
- **OS**: Real CP/M 2.2 binary (CCP + BDOS run as actual 8080 code)
- **BIOS**: Implemented in .NET via OUT-instruction trap mechanism

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [Node.js 20+](https://nodejs.org/)
- CP/M 2.2 binary (see below)

## Obtaining CP/M 2.2

CP/M 2.2 was released as open source by Caldera in 2001. The binary can be obtained from:

1. **RunCPM project** - includes pre-built CP/M binaries:
   ```
   https://github.com/MockbaTheBorg/RunCPM
   ```

2. **Build from source** - The Digital Research source code is at:
   ```
   https://github.com/caldera-corp/CalderaCPM
   ```

3. **Alternative**: Download from the SIMH archive or retro-computing sites.

Place the CCP+BDOS binary as `roms/cpm22.sys`. The file should be approximately 7.5KB.

The binary layout expected:
- Bytes 0x0000-0x07FF: CCP (Console Command Processor)
- Bytes 0x0800+:       BDOS (Basic Disk Operating System)

## Obtaining CP/M Programs

Place `.COM` files in the `roms/` directory to include them on Drive A. Useful programs:

| File | Description | Source |
|------|-------------|--------|
| `MBASIC.COM` | Microsoft BASIC 5.21 | RunCPM releases |
| `ASM.COM` | Intel 8080 Assembler | DR sources |
| `ED.COM` | CP/M Line Editor | DR sources |
| `PIP.COM` | Peripheral Interchange Program | DR sources |
| `STAT.COM` | Disk/File Statistics | DR sources |
| `DDT.COM` | Dynamic Debugging Tool | DR sources |

## Setup

### 1. Build disk image

```bash
dotnet run --project tools/MkDisk
```

This creates `disks/cpm22_system.dsk` with CP/M system tracks and any `.COM` files found in `roms/`.

### 2. Start the backend

```bash
dotnet run --project src/Server
```

The server starts on `http://localhost:5000`.

### 3. Start the frontend (development)

```bash
cd src/Frontend
npm install
npm run dev
```

Navigate to `http://localhost:5173`

### 4. Production build

```bash
cd src/Frontend && npm run build  # outputs to src/Server/wwwroot
dotnet run --project src/Server   # serves everything from :5000
```

## Terminal Usage

Once running, the CP/M prompt (`A>`) appears in the terminal.

**Basic CP/M commands:**
```
A>DIR              - List files on current drive
A>TYPE FILE.TXT    - Display a file
A>MBASIC           - Start BASIC interpreter
A>ASM PROG         - Assemble PROG.ASM → PROG.COM
A>ED PROG.ASM      - Edit PROG.ASM with the line editor
A>PIP B:=A:*.COM   - Copy all COM files to drive B
```

**ED (Line Editor) commands:**
```
*I          - Insert mode (type text, end with Ctrl-Z)
*T          - Type (display) current line
*N          - Next line
*1T         - Type from line 1
*#T         - Type all lines
*E          - Exit and save
*Q          - Quit without saving
```

**MBASIC commands:**
```
10 PRINT "HELLO, WORLD!"
20 GOTO 10
RUN
LIST
SAVE "PROG.BAS"
LOAD "PROG.BAS"
SYSTEM           - Return to CP/M
```

## Debug Panel

The right-side panel provides:
- **Control**: Run/Pause/Step/Reset, CPU speed (0.1–10 MHz)
- **Registers**: A, BC, DE, HL, SP, PC, flags (S, Z, AC, P, CY)
- **Memory**: Hex dump with address navigation (auto-follows PC)
- **Disks**: Mount/unmount `.dsk` files, download drive images

## Running Tests

```bash
dotnet test tests/Emulator.Tests
```

Tests cover all 8080 opcodes, flag computation, and CP/M BIOS functions.

## Project Structure

```
i8080_Sonnet46/
├── src/
│   ├── Emulator/          # CPU, Memory, BIOS, Disk (class library)
│   ├── Server/            # ASP.NET Core + SignalR
│   └── Frontend/          # React + xterm.js
├── tests/
│   ├── Emulator.Tests/    # CPU opcode unit tests
│   └── Integration.Tests/ # Full boot integration tests
├── tools/
│   └── MkDisk/            # Disk image builder
├── roms/                  # cpm22.sys + *.COM files (not in git)
└── disks/                 # Generated .dsk files
```

## Technical Notes

### BIOS Trap Mechanism

Instead of emulating actual disk hardware, the BIOS is implemented via OUT-instruction traps:
- At each BIOS jump table entry (0xFA00+), the code is `OUT nn, A; RET`
- The .NET I/O handler intercepts port `nn` (0-16) and executes the BIOS function
- CPU registers are modified as needed, then `RET` returns to the caller

### CP/M Memory Map (64KB)

```
0x0000-0x00FF  Zero page (JMP WBOOT at 0x0000, JMP BDOS at 0x0005)
0x0100-0xCFFF  TPA (Transient Program Area) - programs load here
0xE400-0xEBFF  CCP (Console Command Processor)
0xEC00-0xF9FF  BDOS (Basic Disk Operating System)
0xFA00-0xFFFF  BIOS (jump table + stubs + DPH/DPB structures)
```

### Disk Format

Standard 8" single-density: 77 tracks × 26 sectors × 128 bytes = **256,256 bytes**
