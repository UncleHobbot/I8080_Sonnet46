# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

### Build
```bash
dotnet build                          # build all projects
dotnet build src/Emulator/            # build just the emulator library
```

### Test
```bash
dotnet test tests/Emulator.Tests/                              # 36 CPU opcode unit tests
dotnet test tests/Integration.Tests/                           # 2 headless CP/M boot tests
dotnet test tests/Emulator.Tests/ --filter "DisplayName~DAA"   # run a single test by name
```

Integration tests require `roms/cpm22.sys` and `disks/cpm22_system.dsk` to exist. They use `Xunit.SkippableFact` (`Skip.If`) so they skip gracefully when those files are absent. The running server process locks `Emulator.dll` — stop it before rebuilding.

### Run
```bash
# Terminal 1 — backend (auto-loads roms/cpm22.sys and disks/cpm22_system.dsk)
dotnet run --project src/Server --urls "http://localhost:5000"

# Terminal 2 — frontend
cd src/Frontend && npm run dev        # http://localhost:5173
```

### Build disk image (required after adding/removing COM files in roms/)
```bash
dotnet run --project tools/MkDisk -- "$(pwd)/roms" "$(pwd)/disks"
```

This creates `disks/cpm22_system.dsk` (256,256 bytes). Both `roms/*.sys`, `roms/*.COM`, and `disks/*.dsk` are git-ignored.

### Obtain CP/M binary
Concatenate CCP + BDOS from the `ivop/cpm22-from-source` GitHub release:
```bash
cat bin/ccp.sys bin/bdos.sys > roms/cpm22.sys   # result: 5632 bytes
```

---

## Architecture

### How CP/M execution works

The emulator runs the **real CP/M 2.2 binary** (`cpm22.sys`, 5632 bytes = CCP 2KB + BDOS 3.5KB) as actual 8080 machine code. The .NET code implements only the BIOS layer.

**BIOS trap mechanism** — `CpmSystem.Initialize()` writes into memory:
- **Jump table** at `0xFA00`: 17 × 3-byte `JMP stubN` entries
- **Stubs** at `0xFA33`: 17 × 3-byte `OUT fnIndex, A` + `RET`

When CP/M calls a BIOS entry (e.g. CONOUT via `CALL 0xFA0C`), the JMP fires the stub, the `OUT` instruction is caught by `IoPort.Out()`, dispatched to `BiosHandler.Execute(BiosFunction, cpu)`, and `RET` returns to the caller. No emulated hardware; pure .NET side-effects.

### CP/M memory layout (64KB)
```
0x0000–0x00FF   Zero page: JMP WBOOT @0x0000, JMP BDOS @0x0005, CDISK @0x0004
0x0100–0xCFFF   TPA — CP/M programs load and run here
0xE400–0xEBFF   CCP  (loaded from cpm22.sys bytes 0x0000–0x07FF)
0xEC00–0xF9FF   BDOS (loaded from cpm22.sys bytes 0x0800+)
0xFA00–0xFA32   BIOS jump table (17 × JMP)
0xFA33–0xFA82   BIOS stubs (17 × OUT fn + RET)
0xFB00+         DPH (×4 drives), DPB, DIRBUF, CSV, ALV
```
All constants live in `MemoryMap.cs`.

### WBOOT entry point
Cold boot enters CCP at `CCP_BASE` (0xE400). Warm boot **must** enter at `CCP_BASE + 3` (0xE403) — the second JMP vector in the cpm22.sys header. Getting this wrong causes spurious `Bdos Err On X: Select` errors.

### Key data flows

**CONIN (blocking input):** `BiosHandler.CONIN` calls `TerminalInputQueue.BlockingRead()` which blocks on `BlockingCollection<byte>.Take(CancellationToken)`. The SignalR hub's `SendInput` method enqueues bytes. The cancel token is linked to the reset path to avoid deadlock.

**CONOUT (output batching):** Each CONOUT char goes to `EmulatorService._outputBuffer`. A `Task.Delay(8)` flush timer batches chars before a single `SignalR.SendAsync("ReceiveOutput")` call (~120fps). Bare `\r` is translated to `\r\n` for VT100.

**CPU throttling:** `EmulatorService.CpuLoop` runs 10,000-cycle batches and sleeps when ahead of the wall-clock target (default 2 MHz, configurable 100 KHz–10 MHz via `SetSpeed`).

**Disk I/O:** BIOS SELDSK returns the DPH address for a drive (or 0 if unmounted). SETTRK/SETSEC/SETDMA set state in `DiskSystem`. READ/WRITE copy 128 bytes between `MemoryBus` at the DMA address and `DiskImage` sector storage. Disk geometry: 77 tracks × 26 sectors × 128 bytes = 256,256 bytes.

### Project map

| Project | Role |
|---|---|
| `src/Emulator/` | Class library: CPU, memory, BIOS, disk, orchestrator |
| `src/Server/` | ASP.NET Core 10 + SignalR hub + CPU thread |
| `src/Frontend/` | React + Vite + TypeScript + xterm.js |
| `tests/Emulator.Tests/` | CPU opcode unit tests (no ROM needed) |
| `tests/Integration.Tests/` | Headless boot tests (ROM + disk required) |
| `tools/MkDisk/` | Builds cpm22_system.dsk from roms/ contents |

### SignalR protocol

**Client → Server** (hub methods): `SendInput(string)`, `Pause()`, `Resume()`, `Step()`, `Reset()`, `HardReset()`, `SetSpeed(int hz)`, `ReadMemory(int addr, int len)`, `ListDiskFiles(int drive)`, `UploadDisk(int drive, string name, byte[])`, `DownloadDisk(int drive)`, `GetCpuState()`

**Server → Client** (events): `ReceiveOutput(string)`, `CpuStateUpdate(CpuStateDto)`, `DiskActivity(int drive, bool writing)`, `StatusMessage(string level, string text)`

### CPU implementation notes
- `I8080.Instructions.cs` — all ~246 opcodes in a dense `switch` (JIT generates a jump table)
- `[MethodImpl(AggressiveOptimization)]` on `Dispatch()`, `[AggressiveInlining]` on `ExecuteOne()`
- `Flags.cs` — 256-entry pre-built parity table; `PackFlags`/`UnpackFlags` for PUSH/POP PSW (bit 1 always 1, bits 5/3 always 0)
- Disassembler helpers are named `ByteAt()`/`WordAt()`/`DReg()` — **not** `B()`/`W()`/`Reg()` which would collide with register fields
- `INR`/`DCR` do **not** affect CY; `DAD` only sets CY; `DAA` is the most complex opcode
