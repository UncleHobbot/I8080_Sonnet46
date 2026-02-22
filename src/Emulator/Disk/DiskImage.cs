namespace Emulator.Disk;

/// <summary>
/// Represents a CP/M 2.2 standard 8-inch single-density floppy disk image.
/// Geometry: 77 tracks × 26 sectors × 128 bytes = 256,256 bytes.
/// </summary>
public sealed class DiskImage
{
    public const int TRACKS      = 77;
    public const int SECTORS     = 26;  // per track
    public const int SECTOR_SIZE = 128; // bytes per sector
    public const int DISK_SIZE   = TRACKS * SECTORS * SECTOR_SIZE; // 256,256

    private readonly byte[] _data;

    public string Name { get; }
    public bool IsModified { get; private set; }

    public DiskImage(string name, byte[] data)
    {
        if (data.Length != DISK_SIZE)
            throw new ArgumentException(
                $"Disk image '{name}' must be exactly {DISK_SIZE} bytes, got {data.Length}");
        Name = name;
        _data = data;
    }

    public static DiskImage CreateBlank(string name) =>
        new(name, new byte[DISK_SIZE]);

    public static DiskImage LoadFromFile(string path) =>
        new(Path.GetFileName(path), File.ReadAllBytes(path));

    public static DiskImage LoadFromBytes(string name, byte[] data) =>
        new(name, (byte[])data.Clone());

    /// <summary>
    /// Read a 128-byte sector. Track is 0-based, sector is 1-based (CP/M convention).
    /// </summary>
    public ReadOnlySpan<byte> ReadSector(int track, int sector)
    {
        int offset = SectorOffset(track, sector);
        return _data.AsSpan(offset, SECTOR_SIZE);
    }

    /// <summary>
    /// Write a 128-byte sector. Track is 0-based, sector is 1-based.
    /// </summary>
    public void WriteSector(int track, int sector, ReadOnlySpan<byte> data)
    {
        int offset = SectorOffset(track, sector);
        data.CopyTo(_data.AsSpan(offset, SECTOR_SIZE));
        IsModified = true;
    }

    public byte[] ToArray() => (byte[])_data.Clone();

    private static int SectorOffset(int track, int sector) =>
        ((track * SECTORS) + (sector - 1)) * SECTOR_SIZE;
}
