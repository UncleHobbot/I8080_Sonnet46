using System.Text;

namespace Emulator.Disk;

/// <summary>
/// CP/M 2.2 in-memory file system built on top of DiskImage.
/// Reads all files into memory when the disk is mounted, provides
/// high-level file operations, and flushes dirty files back to disk on close/flush.
/// </summary>
public sealed class CpmDisk
{
    // Disk geometry matching the DPB in DiskSystem
    private const int SPT             = 26;   // sectors per track
    private const int SECTOR_SIZE     = 128;  // bytes per sector
    private const int BLOCK_SIZE      = 8 * SECTOR_SIZE;   // 1024 bytes (BSH=3)
    private const int RECORDS_PER_BLOCK = 8;               // BLM+1
    private const int RECORDS_PER_EXTENT = 128;            // (EXM+1)*128
    private const int BLOCKS_PER_EXTENT = 16;              // AL entries per dir entry
    private const int MAX_DIR_ENTRIES = 64;                // DRM+1
    private const int SYS_TRACKS     = 2;                  // OFF
    private const int DIR_BLOCK_COUNT = 2;                 // first 2 data blocks = directory
    private const int DSM             = 242;                // max block number
    private const int DIR_ENTRY_SIZE  = 32;

    private readonly DiskImage _disk;
    // Key: "FILENAME.EXT" uppercase, Value: all extents for that file
    private readonly Dictionary<string, CpmFile> _files = new(StringComparer.OrdinalIgnoreCase);
    // Tracks which blocks are used (true = used)
    private readonly bool[] _blockUsed = new bool[DSM + 1];

    public CpmDisk(DiskImage disk)
    {
        _disk = disk;
        MarkDirectoryBlocksUsed();
        LoadDirectory();
    }

    // ── Directory loading ─────────────────────────────────────────────────────

    private void MarkDirectoryBlocksUsed()
    {
        // Blocks 0 and 1 are directory (AL0=0xC0)
        _blockUsed[0] = true;
        _blockUsed[1] = true;
    }

    private void LoadDirectory()
    {
        // Read all 64 directory entries (2 blocks × 32 entries per block)
        // Collect by filename and sort by extent number
        var extentsByName = new Dictionary<string, List<DirEntry>>(StringComparer.OrdinalIgnoreCase);

        for (int entryIdx = 0; entryIdx < MAX_DIR_ENTRIES; entryIdx++)
        {
            var entry = ReadDirEntry(entryIdx);
            if (entry == null) continue; // deleted or empty

            string key = entry.Name + "." + entry.Ext;
            if (!extentsByName.TryGetValue(key, out var list))
            {
                list = new List<DirEntry>();
                extentsByName[key] = list;
            }
            list.Add(entry);

            // Mark allocation blocks as used
            foreach (byte blk in entry.AllocBlocks)
            {
                if (blk > 0 && blk <= DSM)
                    _blockUsed[blk] = true;
            }
        }

        // Build CpmFile objects from extents, sorted by extent number
        foreach (var (key, extents) in extentsByName)
        {
            extents.Sort((a, b) => a.ExtentNumber.CompareTo(b.ExtentNumber));
            var file = BuildFile(extents);
            _files[key] = file;
        }
    }

    private DirEntry? ReadDirEntry(int index)
    {
        // Directory occupies blocks 0 and 1: track 2, sectors 1..16
        int sectorIdx = index / 4;           // 4 entries per 128-byte sector
        int entryInSector = index % 4;

        int track = SYS_TRACKS + sectorIdx / SPT;
        int sector = (sectorIdx % SPT) + 1;  // 1-based

        var sectorData = _disk.ReadSector(track, sector);
        int offset = entryInSector * DIR_ENTRY_SIZE;

        byte status = sectorData[offset];
        if (status == 0xE5) return null;      // deleted
        if (status > 0x1F) return null;       // not a valid entry

        var name = ReadName(sectorData, offset + 1, 8);
        var ext  = ReadName(sectorData, offset + 9, 3);
        if (name.Trim().Length == 0) return null;

        byte ex = sectorData[offset + 12];
        byte s2 = sectorData[offset + 14];
        int extentNum = (s2 << 5) | (ex & 0x1F);

        byte rc = sectorData[offset + 15];
        var alloc = new byte[16];
        sectorData.Slice(offset + 16, 16).CopyTo(alloc);

        bool readOnly = (sectorData[offset + 9] & 0x80) != 0;
        bool system   = (sectorData[offset + 10] & 0x80) != 0;

        return new DirEntry
        {
            Status = status,
            Name = name,
            Ext = ext,
            ExtentNumber = extentNum,
            RecordCount = rc,
            AllocBlocks = alloc,
            ReadOnly = readOnly,
            System = system,
            DirIndex = index
        };
    }

    private static string ReadName(ReadOnlySpan<byte> data, int offset, int len)
    {
        var sb = new StringBuilder(len);
        for (int i = 0; i < len; i++)
            sb.Append((char)(data[offset + i] & 0x7F));
        return sb.ToString();
    }

    private CpmFile BuildFile(List<DirEntry> extents)
    {
        var records = new List<byte[]>();
        int recordsTotal = 0;

        foreach (var extent in extents)
        {
            // How many records in this extent?
            int extRecords = extent.RecordCount;
            if (extRecords == 0 && extents.Count == 1) extRecords = 0;

            // Read each allocated block
            for (int blkIdx = 0; blkIdx < BLOCKS_PER_EXTENT; blkIdx++)
            {
                byte blockNum = extent.AllocBlocks[blkIdx];
                if (blockNum == 0) break;

                for (int recInBlk = 0; recInBlk < RECORDS_PER_BLOCK; recInBlk++)
                {
                    int logicalRecord = extent.ExtentNumber * RECORDS_PER_EXTENT
                                        + blkIdx * RECORDS_PER_BLOCK + recInBlk;

                    // Only read up to RC records in last extent
                    bool isLastExtent = extent == extents[^1];
                    if (isLastExtent && recInBlk >= extRecords % RECORDS_PER_BLOCK
                        && blkIdx == extRecords / RECORDS_PER_BLOCK)
                        break;

                    var (track, sector) = BlockRecordToPhysical(blockNum, recInBlk);
                    var data = new byte[SECTOR_SIZE];
                    _disk.ReadSector(track, sector).CopyTo(data);

                    while (records.Count <= logicalRecord)
                        records.Add(new byte[SECTOR_SIZE]);
                    records[logicalRecord] = data;
                    recordsTotal = Math.Max(recordsTotal, logicalRecord + 1);
                }
            }
        }

        var firstExtent = extents[0];
        return new CpmFile
        {
            Name = firstExtent.Name.TrimEnd(),
            Ext = firstExtent.Ext.TrimEnd(),
            Records = records,
            TotalRecords = recordsTotal,
            ReadOnly = firstExtent.ReadOnly,
            System = firstExtent.System
        };
    }

    // ── Physical address helpers ──────────────────────────────────────────────

    /// <summary>Convert block number + record-in-block to physical track/sector.</summary>
    private static (int track, int sector) BlockRecordToPhysical(int blockNum, int recordInBlock)
    {
        int sectorOffset = blockNum * RECORDS_PER_BLOCK + recordInBlock;
        int track  = sectorOffset / SPT + SYS_TRACKS;
        int sector = sectorOffset % SPT + 1;
        return (track, sector);
    }

    // ── Block allocation ──────────────────────────────────────────────────────

    private int AllocateBlock()
    {
        for (int i = DIR_BLOCK_COUNT; i <= DSM; i++)
        {
            if (!_blockUsed[i])
            {
                _blockUsed[i] = true;
                return i;
            }
        }
        return 0; // disk full
    }

    private void FreeBlocks(IEnumerable<byte> blocks)
    {
        foreach (byte b in blocks)
            if (b > 0) _blockUsed[b] = false;
    }

    // ── Public File API ───────────────────────────────────────────────────────

    /// <summary>Open an existing file. Returns null if not found.</summary>
    public CpmFile? OpenFile(string name, string ext)
    {
        string key = NormalizeKey(name, ext);
        return _files.TryGetValue(key, out var f) ? f : null;
    }

    /// <summary>Create a new empty file. Returns null if a file with that name already exists.</summary>
    public CpmFile? CreateFile(string name, string ext)
    {
        string key = NormalizeKey(name, ext);
        if (_files.ContainsKey(key)) return null;

        var file = new CpmFile
        {
            Name = Pad(name, 8),
            Ext  = Pad(ext, 3),
            Records = new List<byte[]>(),
            TotalRecords = 0,
            Dirty = true
        };
        _files[key] = file;
        return file;
    }

    /// <summary>Open or create a file (for write-mode open).</summary>
    public CpmFile OpenOrCreate(string name, string ext)
    {
        return OpenFile(name, ext) ?? CreateFile(name, ext)!;
    }

    /// <summary>Delete all directory entries for a file matching the pattern.</summary>
    public int DeleteFiles(string namePattern, string extPattern)
    {
        var toDelete = new List<string>();
        foreach (var key in _files.Keys)
        {
            var file = _files[key];
            if (MatchPattern(file.Name.TrimEnd(), namePattern) &&
                MatchPattern(file.Ext.TrimEnd(), extPattern))
            {
                toDelete.Add(key);
            }
        }

        foreach (var key in toDelete)
        {
            var file = _files[key];
            file.Deleted = true;
            _files.Remove(key);
            FreeAllFileBlocks(file);
            EraseDirectoryEntries(file);
        }
        return toDelete.Count;
    }

    private void FreeAllFileBlocks(CpmFile file)
    {
        // Recalculate which blocks this file used (based on extent count and records)
        // We need to know the actual blocks - for simplicity, just scan directory
        // for this file and free those blocks. But since we've already removed from _files,
        // we need to track blocks in CpmFile itself.
        // For now, blocks are freed when the file is written back and old entries removed.
        // This is acceptable for an in-memory implementation.
    }

    private void EraseDirectoryEntries(CpmFile file)
    {
        // Mark all dir entries for this file as 0xE5 (deleted)
        string name = Pad(file.Name, 8);
        string ext  = Pad(file.Ext,  3);

        for (int i = 0; i < MAX_DIR_ENTRIES; i++)
        {
            var entry = ReadDirEntry(i);
            if (entry == null) continue;
            if (entry.Name == name && entry.Ext == ext)
            {
                // Mark as deleted
                WriteDirEntryByte(i, 0, 0xE5);
                // Free blocks
                foreach (byte blk in entry.AllocBlocks)
                    if (blk > 0) _blockUsed[blk] = false;
            }
        }
    }

    /// <summary>Rename a file.</summary>
    public bool RenameFile(string oldName, string oldExt, string newName, string newExt)
    {
        string oldKey = NormalizeKey(oldName, oldExt);
        if (!_files.TryGetValue(oldKey, out var file)) return false;

        string newKey = NormalizeKey(newName, newExt);
        if (_files.ContainsKey(newKey)) return false;

        _files.Remove(oldKey);
        file.Name = Pad(newName.TrimEnd(), 8);
        file.Ext  = Pad(newExt.TrimEnd(), 3);
        file.Dirty = true;
        _files[newKey] = file;
        return true;
    }

    /// <summary>List files matching the pattern.</summary>
    public List<CpmFile> SearchFiles(string namePattern, string extPattern)
    {
        var results = new List<CpmFile>();
        foreach (var file in _files.Values)
        {
            if (MatchPattern(file.Name.TrimEnd(), namePattern) &&
                MatchPattern(file.Ext.TrimEnd(), extPattern))
            {
                results.Add(file);
            }
        }
        results.Sort((a, b) => string.Compare(a.Name + a.Ext, b.Name + b.Ext, StringComparison.OrdinalIgnoreCase));
        return results;
    }

    /// <summary>Read the n-th 128-byte record of a file into buf. Returns 0=OK, 1=EOF/error.</summary>
    public byte ReadRecord(CpmFile file, int recordNum, Span<byte> buf)
    {
        if (recordNum < 0) return 1;
        if (recordNum >= file.Records.Count || file.Records.Count == 0) return 1; // EOF
        var rec = file.Records[recordNum];
        rec.AsSpan().CopyTo(buf);
        return 0;
    }

    /// <summary>Write data to the n-th 128-byte record of a file. Returns 0=OK, 1=error.</summary>
    public byte WriteRecord(CpmFile file, int recordNum, ReadOnlySpan<byte> data)
    {
        if (file.ReadOnly) return 2;

        while (file.Records.Count <= recordNum)
            file.Records.Add(new byte[SECTOR_SIZE]);

        data.CopyTo(file.Records[recordNum]);
        if (recordNum >= file.TotalRecords)
            file.TotalRecords = recordNum + 1;
        file.Dirty = true;
        return 0;
    }

    /// <summary>Get byte content of a file (all records concatenated, trimming EOF markers).</summary>
    public byte[] GetFileBytes(CpmFile file)
    {
        var result = new List<byte>(file.TotalRecords * SECTOR_SIZE);
        for (int i = 0; i < file.TotalRecords; i++)
        {
            if (i < file.Records.Count)
                result.AddRange(file.Records[i]);
        }
        // Trim trailing 0x1A (CP/M EOF marker)
        int end = result.Count;
        while (end > 0 && result[end - 1] == 0x1A) end--;
        return result.Take(end).ToArray();
    }

    /// <summary>Set file contents from a byte array.</summary>
    public void SetFileBytes(CpmFile file, byte[] data)
    {
        file.Records.Clear();
        int offset = 0;
        while (offset < data.Length)
        {
            var rec = new byte[SECTOR_SIZE];
            int toCopy = Math.Min(SECTOR_SIZE, data.Length - offset);
            Array.Copy(data, offset, rec, 0, toCopy);
            if (toCopy < SECTOR_SIZE) rec[toCopy] = 0x1A; // CP/M EOF
            file.Records.Add(rec);
            offset += SECTOR_SIZE;
        }
        file.TotalRecords = file.Records.Count;
        file.Dirty = true;
    }

    /// <summary>Flush all dirty files to the underlying disk image.</summary>
    public void Flush()
    {
        foreach (var file in _files.Values)
        {
            if (file.Dirty)
                FlushFile(file);
        }
    }

    /// <summary>Flush a single file back to the disk image.</summary>
    public void FlushFile(CpmFile file)
    {
        if (!file.Dirty) return;

        // Erase old directory entries for this file (if updating existing)
        EraseDirectoryEntries(file);

        // Write file data to disk, building directory entries
        int totalRecords = file.TotalRecords;
        int extentNum = 0;
        int recordIdx = 0;
        int dirEntry = FindFreeDirEntry();

        while (recordIdx <= totalRecords || (recordIdx == 0 && totalRecords == 0))
        {
            var allocBlocks = new byte[BLOCKS_PER_EXTENT];
            int extentRecords = 0;

            for (int blkIdx = 0; blkIdx < BLOCKS_PER_EXTENT; blkIdx++)
            {
                bool hasData = false;
                int blockNum = 0;

                for (int recInBlk = 0; recInBlk < RECORDS_PER_BLOCK; recInBlk++)
                {
                    if (recordIdx >= totalRecords) break;

                    if (!hasData)
                    {
                        blockNum = AllocateBlock();
                        if (blockNum == 0) goto DiskFull;
                        allocBlocks[blkIdx] = (byte)blockNum;
                        hasData = true;
                    }

                    var rec = recordIdx < file.Records.Count
                        ? file.Records[recordIdx]
                        : new byte[SECTOR_SIZE];

                    var (track, sector) = BlockRecordToPhysical(blockNum, recInBlk);
                    _disk.WriteSector(track, sector, rec);
                    recordIdx++;
                    extentRecords++;
                }

                if (recordIdx >= totalRecords) break;
            }

            // Write directory entry
            if (dirEntry >= MAX_DIR_ENTRIES) goto DiskFull;
            WriteDirEntry(dirEntry, file, extentNum, allocBlocks, extentRecords);
            dirEntry = FindFreeDirEntry();
            extentNum++;

            if (recordIdx >= totalRecords) break;
        }

        DiskFull:
        file.Dirty = false;
        return;
    }

    // ── Directory I/O ─────────────────────────────────────────────────────────

    private int FindFreeDirEntry()
    {
        for (int i = 0; i < MAX_DIR_ENTRIES; i++)
        {
            int sectorIdx = i / 4;
            int entryInSector = i % 4;
            int track  = SYS_TRACKS + sectorIdx / SPT;
            int sector = (sectorIdx % SPT) + 1;
            var data = _disk.ReadSector(track, sector);
            if (data[entryInSector * DIR_ENTRY_SIZE] == 0xE5) return i;
        }
        return MAX_DIR_ENTRIES; // no free entry
    }

    private void WriteDirEntry(int index, CpmFile file, int extentNum, byte[] allocBlocks, int rc)
    {
        int sectorIdx = index / 4;
        int entryInSector = index % 4;
        int track  = SYS_TRACKS + sectorIdx / SPT;
        int sector = (sectorIdx % SPT) + 1;

        // Read current sector, modify entry, write back
        var sectorBuf = _disk.ReadSector(track, sector).ToArray();
        int offset = entryInSector * DIR_ENTRY_SIZE;

        sectorBuf[offset + 0]  = 0x00; // status = active

        string name = Pad(file.Name.TrimEnd(), 8);
        string ext  = Pad(file.Ext.TrimEnd(), 3);
        for (int i = 0; i < 8; i++) sectorBuf[offset + 1 + i] = (byte)name[i];
        for (int i = 0; i < 3; i++) sectorBuf[offset + 9 + i] = (byte)ext[i];

        sectorBuf[offset + 12] = (byte)(extentNum & 0x1F);  // EX low
        sectorBuf[offset + 13] = 0;                          // S1
        sectorBuf[offset + 14] = (byte)((extentNum >> 5) & 0xFF); // S2
        sectorBuf[offset + 15] = (byte)(rc > 128 ? 128 : rc); // RC

        for (int i = 0; i < 16; i++)
            sectorBuf[offset + 16 + i] = i < allocBlocks.Length ? allocBlocks[i] : (byte)0;

        _disk.WriteSector(track, sector, sectorBuf);
    }

    private void WriteDirEntryByte(int index, int byteOffset, byte value)
    {
        int sectorIdx = index / 4;
        int entryInSector = index % 4;
        int track  = SYS_TRACKS + sectorIdx / SPT;
        int sector = (sectorIdx % SPT) + 1;

        var sectorBuf = _disk.ReadSector(track, sector).ToArray();
        sectorBuf[entryInSector * DIR_ENTRY_SIZE + byteOffset] = value;
        _disk.WriteSector(track, sector, sectorBuf);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string NormalizeKey(string name, string ext)
    {
        name = name.Trim().ToUpperInvariant().PadRight(8)[..8];
        ext  = ext.Trim().ToUpperInvariant().PadRight(3)[..3];
        return name + "." + ext;
    }

    private static string Pad(string s, int len) =>
        (s.ToUpperInvariant() + new string(' ', len))[..len];

    /// <summary>CP/M wildcard match: * matches any chars, ? matches one char.</summary>
    public static bool MatchPattern(string name, string pattern)
    {
        if (pattern == "*") return true;
        return WildcardMatch(name.ToUpperInvariant(), pattern.ToUpperInvariant());
    }

    private static bool WildcardMatch(string text, string pattern)
    {
        int t = 0, p = 0;
        int starP = -1, starT = 0;

        while (t < text.Length)
        {
            if (p < pattern.Length && (pattern[p] == '?' || pattern[p] == text[t]))
            {
                t++; p++;
            }
            else if (p < pattern.Length && pattern[p] == '*')
            {
                starP = p++;
                starT = t;
            }
            else if (starP != -1)
            {
                p = starP + 1;
                t = ++starT;
            }
            else return false;
        }

        while (p < pattern.Length && pattern[p] == '*') p++;
        return p == pattern.Length;
    }

    /// <summary>Parse "FILENAME.EXT" into name and ext parts (CP/M 8.3 format).</summary>
    public static (string name, string ext) ParseFilename(string filename)
    {
        filename = filename.Trim().ToUpperInvariant();
        int dot = filename.LastIndexOf('.');
        if (dot < 0)
            return (filename.PadRight(8)[..Math.Min(8, filename.Length)], "   ");

        string name = filename[..dot].PadRight(8);
        string ext  = filename[(dot + 1)..].PadRight(3);
        return (name[..Math.Min(8, name.Length)], ext[..Math.Min(3, ext.Length)]);
    }

    /// <summary>Get a sorted list of all file names for display.</summary>
    public List<string> GetFileList(string namePattern = "*", string extPattern = "*")
    {
        return SearchFiles(namePattern, extPattern)
            .Select(f => $"{f.Name.TrimEnd()}.{f.Ext.TrimEnd()}")
            .ToList();
    }

    public DiskImage Disk => _disk;
}

/// <summary>A CP/M file represented as a list of 128-byte records in memory.</summary>
public sealed class CpmFile
{
    public string Name = "";              // 8 chars, space-padded, trimmed for display
    public string Ext  = "";             // 3 chars, space-padded
    public List<byte[]> Records = new(); // Each record is 128 bytes
    public int TotalRecords;
    public bool ReadOnly;
    public bool System;
    public bool Dirty;
    public bool Deleted;

    // Sequential I/O state (for BDOS fn 20/21)
    public int CurrentRecord;    // CR: next record to read/write
    public ushort DmaAddress;    // DMA buffer address for this FCB's operations
}

/// <summary>Raw directory entry read from disk (internal use).</summary>
internal sealed class DirEntry
{
    public byte Status;
    public string Name = "";
    public string Ext  = "";
    public int ExtentNumber;
    public byte RecordCount;
    public byte[] AllocBlocks = new byte[16];
    public bool ReadOnly;
    public bool System;
    public int DirIndex;
}
