using Emulator.Memory;

namespace Emulator.Disk;

/// <summary>
/// CP/M BIOS disk system: manages 4 virtual drives (A-D),
/// current track/sector/DMA state, and implements READ/WRITE sector.
/// </summary>
public sealed class DiskSystem
{
    private readonly DiskImage?[] _drives = new DiskImage?[4];
    private readonly ushort[] _dphAddresses = new ushort[4];

    private int _selectedDrive;
    private int _currentTrack;
    private int _currentSector;
    private ushort _dmaAddress = MemoryMap.TPA_BASE; // default DMA at 0x0080

    public int SelectedDrive => _selectedDrive;

    // DPB constants for 8" single-density floppy
    // SPT=26, BSH=3, BLM=7, EXM=0, DSM=242, DRM=63, AL0=0xC0, AL1=0x00, CKS=16, OFF=2
    private static readonly byte[] Dpb = {
        26, 0,    // SPT = 26 sectors per track
        3,        // BSH = block shift (2^3 = 8 sectors/block)
        7,        // BLM = block mask
        0,        // EXM = extent mask
        242, 0,   // DSM = 242 (max block number)
        63, 0,    // DRM = 63 (max directory entry)
        0xC0,     // AL0 (bit map for reserved directory blocks)
        0x00,     // AL1
        16, 0,    // CKS = 16
        2, 0      // OFF = 2 (system tracks)
    };

    public void MountDisk(int drive, DiskImage image)
    {
        if (drive is < 0 or > 3)
            throw new ArgumentOutOfRangeException(nameof(drive));
        _drives[drive] = image;
    }

    public void EjectDisk(int drive) => _drives[drive] = null;
    public DiskImage? GetDisk(int drive) => _drives[drive];

    /// <summary>
    /// Initialize DPH/DPB structures into emulator memory.
    /// Must be called after memory is initialized.
    /// </summary>
    public void InitMemoryStructures(MemoryBus mem)
    {
        // Write shared DPB at DPB_BASE
        mem.Load(MemoryMap.DPB_BASE, Dpb);

        // Write DPH for each drive (16 bytes each) at DPH_BASE
        for (int d = 0; d < 4; d++)
        {
            int dphAddr = MemoryMap.DPH_BASE + (d * 16);
            _dphAddresses[d] = (ushort)dphAddr;

            // XLT: translation table address (0 = no translation for 8" SD)
            mem.WriteWord((ushort)(dphAddr + 0), 0x0000);
            // Scratch words (3 of them)
            mem.WriteWord((ushort)(dphAddr + 2), 0x0000);
            mem.WriteWord((ushort)(dphAddr + 4), 0x0000);
            mem.WriteWord((ushort)(dphAddr + 6), 0x0000);
            // DIRBUF: shared 128-byte directory buffer
            mem.WriteWord((ushort)(dphAddr + 8), MemoryMap.DIRBUF_BASE);
            // DPB: disk parameter block
            mem.WriteWord((ushort)(dphAddr + 10), MemoryMap.DPB_BASE);
            // CSV: checksum vector
            mem.WriteWord((ushort)(dphAddr + 12), MemoryMap.CSV_BASE);
            // ALV: allocation vector (32 bytes for DSM=242)
            mem.WriteWord((ushort)(dphAddr + 14), (ushort)(MemoryMap.ALV_BASE + d * 32));
        }
    }

    /// <summary>BIOS SELDSK: returns DPH address for the selected drive, or 0 if no disk.</summary>
    public ushort SelectDisk(byte drive)
    {
        if (drive > 3 || _drives[drive] == null)
            return 0;
        _selectedDrive = drive;
        return _dphAddresses[drive];
    }

    public void SetTrack(ushort track) => _currentTrack = track;
    public void SetSector(ushort sector) => _currentSector = sector;
    public void SetDma(ushort addr) => _dmaAddress = addr;

    /// <summary>BIOS READ: read current sector into DMA buffer. Returns 0=OK, 1=error.</summary>
    public byte ReadSector(MemoryBus mem)
    {
        var disk = _drives[_selectedDrive];
        if (disk == null) return 1;

        try
        {
            var data = disk.ReadSector(_currentTrack, _currentSector);
            mem.WriteBlock(_dmaAddress, data);
            return 0;
        }
        catch
        {
            return 1;
        }
    }

    /// <summary>BIOS WRITE: write DMA buffer to current sector. Returns 0=OK, 1=error.</summary>
    public byte WriteSector(MemoryBus mem)
    {
        var disk = _drives[_selectedDrive];
        if (disk == null) return 1;

        try
        {
            Span<byte> buf = stackalloc byte[DiskImage.SECTOR_SIZE];
            mem.ReadBlock(_dmaAddress, buf);
            disk.WriteSector(_currentTrack, _currentSector, buf);
            return 0;
        }
        catch
        {
            return 1;
        }
    }
}
