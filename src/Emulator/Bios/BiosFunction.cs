namespace Emulator.Bios;

/// <summary>
/// Intel CP/M 2.2 BIOS function codes.
/// Each corresponds to an OUT port instruction placed at the BIOS stub area.
/// </summary>
public enum BiosFunction : byte
{
    BOOT    = 0,   // Cold boot
    WBOOT   = 1,   // Warm boot (reload CCP from disk)
    CONST   = 2,   // Console status: A=0xFF if char ready, A=0x00 if not
    CONIN   = 3,   // Console input: returns char in A (BLOCKING)
    CONOUT  = 4,   // Console output: char in C
    LIST    = 5,   // List (printer) output: char in C
    PUNCH   = 6,   // Punch (paper tape) output: char in C
    READER  = 7,   // Reader (paper tape) input: char in A
    HOME    = 8,   // Move disk head to track 0
    SELDSK  = 9,   // Select disk: drive in C, returns HL=DPH address (or 0)
    SETTRK  = 10,  // Set track: BC = track number
    SETSEC  = 11,  // Set sector: BC = sector number (physical, after SECTRAN)
    SETDMA  = 12,  // Set DMA address: BC = buffer address
    READ    = 13,  // Read sector: A=0 success, A=1 error
    WRITE   = 14,  // Write sector: A=0 success, A=1 error
    LISTST  = 15,  // List status: A=0xFF if ready
    SECTRAN = 16,  // Sector translate: BC=logical, DE=xlat table, HL=physical
}
