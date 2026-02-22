/// <summary>
/// MkDisk: Creates a CP/M 2.2 compatible disk image for the 8080 emulator.
///
/// Usage: MkDisk [romsDir] [disksDir]
///   - Reads cpm22.sys from romsDir (CP/M CCP+BDOS binary)
///   - Creates cpm22_system.dsk in disksDir with the CP/M files
///   - If COM files are present in romsDir, copies them to the disk image
///
/// The disk image is a standard 8" single-density format:
///   77 tracks × 26 sectors × 128 bytes = 256,256 bytes
///
/// CP/M directory format (16-byte entries):
///   [0]    status (0x00 = active, 0xE5 = deleted)
///   [1-8]  filename (space padded)
///   [9-11] extension (space padded)
///   [12]   extent low
///   [13]   reserved
///   [14]   extent high
///   [15]   record count (RC: 128-byte records in this extent)
///   [16-31] allocation blocks (1 byte each for DSM≤255)
/// </summary>

const int TRACKS      = 77;
const int SECTORS     = 26;
const int SECTOR_SIZE = 128;
const int DISK_SIZE   = TRACKS * SECTORS * SECTOR_SIZE; // 256,256
const int SYS_TRACKS  = 2;   // tracks 0-1 reserved for CP/M system
const int DIR_SECTOR_START = SYS_TRACKS * SECTORS + 1; // first directory sector (1-based)
const int BLOCK_SIZE  = 8 * SECTOR_SIZE; // 1KB blocks (BSH=3, BLM=7)
const int DIR_BLOCKS  = 2;   // AL0=0xC0 → 2 blocks reserved for directory
const int DATA_BLOCK_START = SYS_TRACKS * SECTORS / 8 + DIR_BLOCKS; // first data block index

// Resolve paths
string romsDir  = args.Length > 0 ? args[0] : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../roms"));
string disksDir = args.Length > 1 ? args[1] : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../disks"));

Console.WriteLine($"ROMs directory:  {romsDir}");
Console.WriteLine($"Disks directory: {disksDir}");

Directory.CreateDirectory(disksDir);

var cpmBinPath = Path.Combine(romsDir, "cpm22.sys");
if (!File.Exists(cpmBinPath))
{
    Console.Error.WriteLine($"ERROR: cpm22.sys not found at {cpmBinPath}");
    Console.Error.WriteLine("Please obtain CP/M 2.2 binary and place it as roms/cpm22.sys");
    Console.Error.WriteLine("The binary should be the CCP+BDOS image, ~7.5KB");
    return 1;
}

// Create blank 256KB disk image
var disk = new byte[DISK_SIZE];
// Fill with 0xE5 (CP/M deleted-entry marker, also used as blank disk filler)
Array.Fill(disk, (byte)0xE5);

// Write CP/M system to tracks 0-1 (first 6,656 bytes)
var cpmBin = File.ReadAllBytes(cpmBinPath);
Console.WriteLine($"CP/M binary: {cpmBin.Length} bytes");
int sysAreaSize = SYS_TRACKS * SECTORS * SECTOR_SIZE;
Buffer.BlockCopy(cpmBin, 0, disk, 0, Math.Min(cpmBin.Length, sysAreaSize));

// Initialize directory area (tracks 2-3 for 8" SD, blocks 0-1)
// Fill directory with 0xE5 (empty entries)
int dirStart = SYS_TRACKS * SECTORS * SECTOR_SIZE;
int dirSize  = DIR_BLOCKS * BLOCK_SIZE;
Array.Fill(disk, (byte)0xE5, dirStart, dirSize);

// Track allocation state (next block to allocate)
int nextDataBlock = DIR_BLOCKS; // blocks 0-1 are directory

// Copy COM files from romsDir into the disk image
var comFiles = Directory.GetFiles(romsDir, "*.COM")
    .Concat(Directory.GetFiles(romsDir, "*.com"))
    .Select(Path.GetFullPath)
    .Distinct()
    .ToList();

Console.WriteLine($"Found {comFiles.Count} COM files to add to disk A:");

int dirEntryIndex = 0; // current directory slot

foreach (var comPath in comFiles)
{
    var data = File.ReadAllBytes(comPath);
    var name = Path.GetFileNameWithoutExtension(comPath).ToUpperInvariant().PadRight(8);
    var ext  = Path.GetExtension(comPath).TrimStart('.').ToUpperInvariant().PadRight(3);
    if (name.Length > 8) name = name[..8];
    if (ext.Length > 3)  ext  = ext[..3];

    Console.WriteLine($"  {name.TrimEnd()}.{ext.TrimEnd()} ({data.Length} bytes)");

    // Write file data into disk sectors and create directory entries
    int remaining = data.Length;
    int offset = 0;
    bool firstExtent = true;

    while (remaining > 0 || firstExtent)
    {
        // Each directory entry covers one extent (16KB = 128 blocks... wait,
        // for 1KB blocks: 1 extent = 16 allocation bytes = 16 blocks = 16KB)
        int extentSize = Math.Min(remaining, 16 * BLOCK_SIZE); // 16KB per extent
        int blocksNeeded = (extentSize + BLOCK_SIZE - 1) / BLOCK_SIZE;
        if (blocksNeeded == 0 && !firstExtent) break;
        if (blocksNeeded == 0) blocksNeeded = 0; // empty file

        // Allocate blocks and write data
        var allocBlocks = new byte[16];
        for (int b = 0; b < blocksNeeded; b++)
        {
            int block = nextDataBlock++;
            allocBlocks[b] = (byte)block;

            // Write up to BLOCK_SIZE bytes from file data into this block
            int blockOffset = SYS_TRACKS * SECTORS * SECTOR_SIZE + block * BLOCK_SIZE;
            int toCopy = Math.Min(BLOCK_SIZE, remaining - b * BLOCK_SIZE);
            if (toCopy > 0)
            {
                Buffer.BlockCopy(data, offset + b * BLOCK_SIZE, disk, blockOffset, toCopy);
                // Pad rest of block with 0x1A (CP/M EOF)
                if (toCopy < BLOCK_SIZE)
                    Array.Fill(disk, (byte)0x1A, blockOffset + toCopy, BLOCK_SIZE - toCopy);
            }
        }

        // Compute record count (RC) for this extent
        int rc = (extentSize + SECTOR_SIZE - 1) / SECTOR_SIZE;
        if (rc > 128) rc = 128;

        // Write directory entry at next slot
        int dirEntryOffset = dirStart + dirEntryIndex * 32;
        disk[dirEntryOffset + 0]  = 0x00; // status = active, drive A
        for (int i = 0; i < 8; i++)
            disk[dirEntryOffset + 1 + i] = (byte)(i < name.Length ? name[i] : ' ');
        for (int i = 0; i < 3; i++)
            disk[dirEntryOffset + 9 + i] = (byte)(i < ext.Length ? ext[i] : ' ');
        disk[dirEntryOffset + 12] = (byte)(firstExtent ? 0 : 1); // extent number
        disk[dirEntryOffset + 13] = 0; // reserved
        disk[dirEntryOffset + 14] = 0; // extent high
        disk[dirEntryOffset + 15] = (byte)rc;
        Buffer.BlockCopy(allocBlocks, 0, disk, dirEntryOffset + 16, 16);

        dirEntryIndex++;
        offset    += extentSize;
        remaining -= extentSize;
        firstExtent = false;

        if (remaining <= 0) break;
    }
}

// Write blank.dsk (empty formatted disk)
var blankPath = Path.Combine(disksDir, "blank.dsk");
var blank = new byte[DISK_SIZE];
Array.Fill(blank, (byte)0xE5);
// Initialize blank directory
Array.Fill(blank, (byte)0xE5, dirStart, dirSize);
File.WriteAllBytes(blankPath, blank);
Console.WriteLine($"Created blank.dsk: {blankPath}");

// Write the system disk
var diskPath = Path.Combine(disksDir, "cpm22_system.dsk");
File.WriteAllBytes(diskPath, disk);
Console.WriteLine($"Created cpm22_system.dsk: {diskPath} ({disk.Length} bytes)");
Console.WriteLine($"Directory entries used: {dirEntryIndex} of 64");
Console.WriteLine("Done!");
return 0;
