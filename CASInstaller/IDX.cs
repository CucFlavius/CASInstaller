namespace CASInstaller;

public class IDX
{
    private const int HEADER_HASH_SIZE = 16;
    private const short VERSION = 7;
    private const byte EXTRA_BYTES = 0;
    public const long ARCHIVE_TOTAL_SIZE_MAXIMUM = 1098437885952;
    public readonly string Path;
    private readonly List<Entry> Data;

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
        Data = [];
    }
    
    public Entry Add(Hash key, int archiveId, int offset, ulong size)
    {
        var entry = new Entry(key, archiveId, offset, (uint)size);
        Data.Add(entry);
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
        EntriesSize = Data.Count * (Spec.Size + Spec.Offset + Spec.Key);
        byte[]? entryBytes = null;
        if (EntriesSize == 0)
        {
            EntriesHash = 0;
        }
        else
        {
            using var entriesMs = new MemoryStream();
            using var entriesBw = new BinaryWriter(entriesMs);
            foreach (var entry in Data)
            {
                entry.Write(entriesBw);
            }

            entriesBw.Flush(); // Ensure all data is written to the MemoryStream
            entriesMs.Position = 0; // Reset the stream position for hashing
            entryBytes = entriesMs.ToArray();
            uint epc = 0;
            uint epb = 0;
            HashAlgo.HashLittle2(entryBytes, entryBytes.Length, ref epc, ref epb);
            EntriesHash = epc;
        }

        // Write entries
        bw.Write(EntriesSize);
        bw.Write(EntriesHash);
        if (entryBytes != null)
            bw.Write(entryBytes);
        
        // Pad to 65536 bytes
        var paddingSize = 65536 - fs.Length;
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
            Data.Add(info);
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
        private readonly Hash key;
        private readonly int ArchiveID;
        private readonly int Offset;
        private readonly uint Size;

        public Entry(Hash key, int archiveId, int offset, uint size)
        {
            this.key = key;
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
            bw.Write(key.Key, 0, 9);
            bw.Write((byte)((ArchiveID >> 2) & 0xFF));
            bw.Write((ArchiveID << 30) | Offset);
            bw.Write(Size);
        }
    }
}