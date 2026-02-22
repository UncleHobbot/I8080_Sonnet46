namespace Emulator.Disk;

/// <summary>
/// Parses CP/M 2.2 directory entries from a disk image.
/// </summary>
public static class CpmFileSystem
{
    private const int SECTOR_SIZE    = 128;
    private const int DIR_ENTRY_SIZE = 32;
    private const int ENTRIES_PER_SECTOR = SECTOR_SIZE / DIR_ENTRY_SIZE;
    private const int SYSTEM_TRACKS = 2;  // tracks 0-1 are system tracks

    /// <summary>
    /// List all active files on the disk.
    /// Returns strings like "MBASIC  .COM" (8.3 format).
    /// </summary>
    public static List<string> ListFiles(DiskImage disk)
    {
        var files = new HashSet<string>();

        // Directory starts at track 2, sector 1
        // For 8" SD: 64 directory entries (DRM=63), 4 entries per sector = 16 sectors
        for (int sector = 1; sector <= 16; sector++)
        {
            var sectorData = disk.ReadSector(SYSTEM_TRACKS, sector);

            for (int e = 0; e < ENTRIES_PER_SECTOR; e++)
            {
                int offset = e * DIR_ENTRY_SIZE;
                byte status = sectorData[offset];

                // Active entry: status = drive (0=A, 1=B, ...) or 0x00
                if (status > 0x0F) continue; // deleted (0xE5) or unused

                // Name: bytes 1-8 (space padded, high bit may be set for attributes)
                string name = "";
                for (int i = 1; i <= 8; i++)
                    name += (char)(sectorData[offset + i] & 0x7F);
                name = name.TrimEnd();

                // Extension: bytes 9-11
                string ext = "";
                for (int i = 9; i <= 11; i++)
                    ext += (char)(sectorData[offset + i] & 0x7F);
                ext = ext.TrimEnd();

                if (name.Length == 0) continue;

                string entry = ext.Length > 0 ? $"{name,-8}.{ext}" : $"{name,-8}";
                files.Add(entry);
            }
        }

        return files.OrderBy(f => f).ToList();
    }
}
