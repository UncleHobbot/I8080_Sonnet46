namespace Emulator.Memory;

public static class MemoryMap
{
    public const int RAM_SIZE        = 0x10000; // 64KB

    // Zero page
    public const int WBOOT_VECTOR   = 0x0000;  // JMP WBOOT
    public const int IOBYTE_ADDR    = 0x0003;
    public const int DRIVE_ADDR     = 0x0004;
    public const int BDOS_VECTOR    = 0x0005;  // OUT BDOS_PORT + RET stub

    // I/O port assignments
    public const int BDOS_PORT      = 17;      // port number for .NET BDOS trap

    // TPA - Transient Program Area
    public const int TPA_BASE       = 0x0100;
    public const int TPA_TOP        = 0xCFFF;

    // CP/M system area (64KB standard layout)
    public const int CCP_BASE       = 0xE400;  // Console Command Processor
    public const int CCP_SIZE       = 0x0800;  // 2KB
    public const int BDOS_BASE      = 0xEC00;  // Basic Disk OS
    public const int BDOS_SIZE      = 0x0E00;  // 3.5KB
    public const int BDOS_ENTRY     = 0xEC06;  // BDOS function entry point

    // BIOS area
    public const int BIOS_BASE      = 0xFA00;  // Basic I/O System jump table
    public const int BIOS_STUBS_BASE= 0xFA33;  // After 17 * 3-byte JMPs
    public const int BIOS_SIZE      = 0x0600;  // 1.5KB

    // DPH/DPB tables written just above the stubs
    public const int DPH_BASE       = 0xFB00;  // Disk Parameter Headers (4 drives)
    public const int DPB_BASE       = 0xFB40;  // Disk Parameter Block (shared)
    public const int DIRBUF_BASE    = 0xFB60;  // 128-byte directory buffer
    public const int CSV_BASE       = 0xFBE0;  // Checksum vector (16 bytes)
    public const int ALV_BASE       = 0xFBF0;  // Allocation vector (32 bytes)
}
