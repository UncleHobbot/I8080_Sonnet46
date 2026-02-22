namespace Emulator.CPU;

public record CpuStateDto(
    byte A,
    byte B, byte C,
    byte D, byte E,
    byte H, byte L,
    ushort SP,
    ushort PC,
    bool FlagS,
    bool FlagZ,
    bool FlagAC,
    bool FlagP,
    bool FlagCY,
    bool Halted,
    bool InterruptsEnabled,
    long TotalCycles,
    bool IsRunning,
    int SpeedHz,
    string? CurrentDisassembly
);
