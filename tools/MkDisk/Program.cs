/// <summary>
/// MkDisk: Creates a blank CP/M 2.2 compatible disk image.
///
/// Usage: MkDisk [disksDir]
///   - Creates blank.dsk and cpm22_system.dsk in disksDir
///   - No external binary required (the emulator has a pure-.NET CP/M)
///   - Optionally copies .COM files from romsDir onto cpm22_system.dsk
///
/// Disk format: 77 tracks × 26 sectors × 128 bytes = 256,256 bytes
/// CP/M 2.2 standard 8-inch single-density.
/// </summary>

const int TRACKS      = 77;
const int SECTORS     = 26;
const int SECTOR_SIZE = 128;
const int DISK_SIZE   = TRACKS * SECTORS * SECTOR_SIZE; // 256,256
const int SYS_TRACKS  = 2;   // tracks 0-1 reserved
const int BLOCK_SIZE  = 8 * SECTOR_SIZE; // 1KB blocks
const int DIR_BLOCKS  = 2;   // blocks 0-1 are directory

string disksDir = args.Length > 0 ? args[0]
    : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../disks"));
string? romsDir = args.Length > 1 ? args[1]
    : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../roms"));
if (!Directory.Exists(romsDir)) romsDir = null;

Console.WriteLine($"Disks directory: {disksDir}");
if (romsDir != null) Console.WriteLine($"ROMs directory:  {romsDir}");

Directory.CreateDirectory(disksDir);

// Create blank formatted disk (all 0xE5)
static byte[] CreateBlankDisk()
{
    var disk = new byte[DISK_SIZE];
    Array.Fill(disk, (byte)0xE5);
    // Initialize directory area (tracks 2-3, blocks 0-1)
    int dirStart = SYS_TRACKS * SECTORS * SECTOR_SIZE;
    int dirSize  = DIR_BLOCKS * BLOCK_SIZE;
    Array.Fill(disk, (byte)0xE5, dirStart, dirSize);
    return disk;
}

// Write blank.dsk
var blankPath = Path.Combine(disksDir, "blank.dsk");
File.WriteAllBytes(blankPath, CreateBlankDisk());
Console.WriteLine($"Created blank.dsk: {blankPath}");

// Create system disk (optionally with .COM files from romsDir)
var sysDisk = CreateBlankDisk();
int nextDataBlock = DIR_BLOCKS; // blocks 0-1 = directory
int dirEntryIndex = 0;
int dirStart2 = SYS_TRACKS * SECTORS * SECTOR_SIZE;

// Copy .COM files if romsDir exists
if (romsDir != null)
{
    var comFiles = Directory.GetFiles(romsDir, "*.COM")
        .Concat(Directory.GetFiles(romsDir, "*.com"))
        .Select(Path.GetFullPath)
        .Distinct()
        .OrderBy(f => f)
        .ToList();

    Console.WriteLine($"Found {comFiles.Count} .COM file(s) to add to disk A:");

    foreach (var comPath in comFiles)
    {
        var data = File.ReadAllBytes(comPath);
        string rawName = Path.GetFileNameWithoutExtension(comPath).ToUpperInvariant();
        string rawExt  = "COM";
        string name = rawName.PadRight(8)[..Math.Min(8, rawName.Length)].PadRight(8);
        string ext  = rawExt.PadRight(3)[..3];

        Console.WriteLine($"  {rawName}.COM ({data.Length} bytes)");

        int remaining = data.Length;
        int offset    = 0;
        int extentNum = 0;

        do
        {
            int extentSize = Math.Min(remaining, 16 * BLOCK_SIZE);
            if (extentSize == 0 && extentNum > 0) break;

            int blocksNeeded = (extentSize + BLOCK_SIZE - 1) / BLOCK_SIZE;
            var allocBlocks  = new byte[16];

            for (int b = 0; b < blocksNeeded; b++)
            {
                int block = nextDataBlock++;
                allocBlocks[b] = (byte)block;
                int blockOffset = SYS_TRACKS * SECTORS * SECTOR_SIZE + block * BLOCK_SIZE;
                int toCopy = Math.Min(BLOCK_SIZE, data.Length - offset - b * BLOCK_SIZE);
                if (toCopy > 0)
                {
                    Buffer.BlockCopy(data, offset + b * BLOCK_SIZE, sysDisk, blockOffset, toCopy);
                    if (toCopy < BLOCK_SIZE)
                        Array.Fill(sysDisk, (byte)0x1A, blockOffset + toCopy, BLOCK_SIZE - toCopy);
                }
            }

            int rc = (extentSize + SECTOR_SIZE - 1) / SECTOR_SIZE;
            if (rc > 128) rc = 128;

            if (dirEntryIndex < 64)
            {
                int entryOffset = dirStart2 + dirEntryIndex * 32;
                sysDisk[entryOffset + 0] = 0x00;
                for (int i = 0; i < 8; i++) sysDisk[entryOffset + 1 + i] = (byte)name[i];
                for (int i = 0; i < 3; i++) sysDisk[entryOffset + 9 + i] = (byte)ext[i];
                sysDisk[entryOffset + 12] = (byte)(extentNum & 0x1F);
                sysDisk[entryOffset + 13] = 0;
                sysDisk[entryOffset + 14] = (byte)((extentNum >> 5) & 0xFF);
                sysDisk[entryOffset + 15] = (byte)rc;
                Buffer.BlockCopy(allocBlocks, 0, sysDisk, entryOffset + 16, 16);
                dirEntryIndex++;
            }

            offset    += extentSize;
            remaining -= extentSize;
            extentNum++;
        }
        while (remaining > 0);
    }
}

var diskPath = Path.Combine(disksDir, "cpm22_system.dsk");
File.WriteAllBytes(diskPath, sysDisk);
Console.WriteLine($"Created cpm22_system.dsk: {diskPath} ({sysDisk.Length} bytes)");
if (dirEntryIndex > 0)
    Console.WriteLine($"Directory entries used: {dirEntryIndex} of 64");
Console.WriteLine("Done!");
return 0;
