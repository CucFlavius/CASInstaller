namespace CASInstaller;

public class IDX
{
    private const int HEADER_HASH_SIZE = 16;
    private const short VERSION = 7;
    private const byte EXTRA_BYTES = 0;
    public const long ARCHIVE_TOTAL_SIZE_MAXIMUM = 1098437885952;
    public readonly string Path;
    private readonly List<Entry> m_sortedRecords;

    public int HeaderHashSize { get; set; } = HEADER_HASH_SIZE;
    public uint HeaderHash { get; set; }
    public short Version { get; set; } = VERSION;
    public byte Bucket { get; set; }
    public byte ExtraBytes { get; set; } = EXTRA_BYTES;
    public EntrySpec Spec { get; set; } = new EntrySpec();
    public long ArchiveTotalSizeMaximum { get; set; } = ARCHIVE_TOTAL_SIZE_MAXIMUM;
    public int EntriesSize { get; set; }
    public uint EntriesHash { get; set; }
    
    public IDX(string path, byte bucket)
    {
        Path = path;
        Bucket = bucket;
        m_sortedRecords = [];
    }
    
    public Entry Add(Hash key, int archiveId, int offset, ulong size)
    {
        var entry = new Entry(key, archiveId, offset, (uint)size);
        m_sortedRecords.Add(entry);
        return entry;
    }
    
    public void Write()
    {
        // Main fs and bw
        using var fs = new FileStream(Path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
        using var bw = new BinaryWriter(fs);
        
        // Build header
        using var headerMs = new MemoryStream();
        using var headerBw = new BinaryWriter(headerMs);
        headerBw.Write(Version);
        headerBw.Write(Bucket);
        headerBw.Write(ExtraBytes);
        Spec.Write(headerBw);
        headerBw.Write(ArchiveTotalSizeMaximum);
        headerBw.Flush(); // Ensure all data is written to the MemoryStream
        headerMs.Position = 0; // Reset the stream position for hashing
        var headerBytes = headerMs.ToArray();
        uint pc = 0;
        uint pb = 0;
        HashAlgo.HashLittle2(headerBytes, headerBytes.Length, ref pc, ref pb);
        HeaderHash = pc;
        
        // Write header
        bw.Write(HeaderHashSize);
        bw.Write(HeaderHash);
        bw.Write(headerBytes);
        bw.BaseStream.Position += 8;    // Padding
        
        // Build entries
        EntriesSize = m_sortedRecords.Count * (Spec.Size + Spec.Offset + Spec.Key);
        byte[]? entryBytes = null;
        if (EntriesSize == 0)
        {
            EntriesHash = 0;
        }
        else
        {
            uint epc = 0;
            uint epb = 0;
            using var entriesMs = new MemoryStream();
            using var entriesBw = new BinaryWriter(entriesMs);
            foreach (var entry in m_sortedRecords)
            {
                using var entryMS = new MemoryStream();
                using var entryBW = new BinaryWriter(entryMS);
                entry.Write(entryBW);
                entryBW.Flush(); // Ensure all data is written to the MemoryStream
                entryMS.Position = 0; // Reset the stream position for hashing
                entryBytes = entryMS.ToArray();

                entriesBw.Write(entryBytes);
                
                HashAlgo.HashLittle2(entryBytes, entryBytes.Length, ref epc, ref epb);
            }
            entriesBw.Flush(); // Ensure all data is written to the MemoryStream
            entriesMs.Position = 0; // Reset the stream position for hashing
            entryBytes = entriesMs.ToArray();
            EntriesHash = epc;
        }

        // Write entries
        bw.Write(EntriesSize);
        bw.Write(EntriesHash);
        if (entryBytes != null)
            bw.Write(entryBytes);
        
        // Pad to 196608 bytes
        var paddingSize = 196608 - fs.Length;
        if (paddingSize > 0)
        {
            var padding = new byte[paddingSize];
            bw.Write(padding);
        }

        bw.Flush();
    }
    
    public void Read()
    {
        using var fs = new FileStream(Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var br = new BinaryReader(fs);
        
        HeaderHashSize = br.ReadInt32();
        HeaderHash = br.ReadUInt32();
        Version = br.ReadInt16();
        Bucket = br.ReadByte();
        ExtraBytes = br.ReadByte();
        Spec = new EntrySpec(br);
        ArchiveTotalSizeMaximum = br.ReadInt64();
        br.BaseStream.Position += 8;    // Padding
        
        EntriesSize = br.ReadInt32();
        EntriesHash = br.ReadUInt32();

        var entryCount = Spec.Size + Spec.Offset + Spec.Key;
        entryCount = EntriesSize / entryCount;

        //int archiveBits = Spec.Offset * 8 - Spec.OffsetBits;          
        //int offsetBits = Spec.OffsetBits;
        
        for (var i = 0; i < entryCount; i++)
        {
            var info = new Entry(br);
            m_sortedRecords.Add(info);
        }
    }

    public class EntrySpec
    {
        public byte Size { get; set; }
        public byte Offset { get; set; }
        public byte Key { get; set; }
        public byte OffsetBits { get; set; }
        
        public EntrySpec()
        {
            Size = 4;
            Offset = 5;
            Key = 9;
            OffsetBits = 30;
        }

        public EntrySpec(byte size, byte offset, byte key, byte offsetBits)
        {
            Size = size;
            Offset = offset;
            Key = key;
            OffsetBits = offsetBits;
        }
        
        public EntrySpec(BinaryReader br)
        {
            Size = br.ReadByte();
            Offset = br.ReadByte();
            Key = br.ReadByte();
            OffsetBits = br.ReadByte();
        }
        
        public void Write(BinaryWriter bw)
        {
            bw.Write(Size);
            bw.Write(Offset);
            bw.Write(Key);
            bw.Write(OffsetBits);
        }
    }
    
    public class Entry
    {
        public readonly Hash Key;
        public readonly int ArchiveID;
        public readonly int Offset;
        public readonly uint Size;

        public Entry(Hash key, int archiveId, int offset, uint size)
        {
            this.Key = key;
            ArchiveID = archiveId;
            Offset = offset;
            Size = size;
        }
        
        public Entry(BinaryReader br)
        {
            var keyBytes = br.ReadBytes(9);
            Array.Resize(ref keyBytes, 16);

            var key = new Hash(keyBytes);

            var indexHigh = br.ReadByte();
            var indexLow = br.ReadInt32BE();

            ArchiveID = (indexHigh << 2 | (byte)((indexLow & 0xC0000000) >> 30));
            Offset = (indexLow & 0x3FFFFFFF);

            Size = br.ReadUInt32();
        }
        
        public void Write(BinaryWriter bw)
        {
            bw.Write(Key.Key, 0, 9);

            // Reconstruct the indexHigh and indexLow values
            byte indexHigh = (byte)(ArchiveID >> 2);
            int indexLow = ((ArchiveID & 0x03) << 30) | (Offset & 0x3FFFFFFF);

            // Write the values in Big Endian format
            bw.Write(indexHigh);        // Write the high byte
            bw.WriteInt32BE(indexLow); // Write the 4-byte integer in Big Endian
            
            bw.Write(Size);
        }
    }
}