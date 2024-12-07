using Spectre.Console;

namespace CASInstaller;

public struct FileIndex
{
    public readonly Dictionary<Hash, IndexEntry> Entries = new();

    public byte[] toc_hash;
    public byte version;
    public byte _11;
    public byte _12;
    public byte blockSizeKb;
    public byte offsetBytes;
    public byte sizeBytes;
    public byte keySizeBytes;
    public byte checksumSize;
    public int numElements;
    public byte[] footerChecksum;

    private FileIndex(byte[] data)
    {
        if (data.Length == 0)
            return;
        
        using var stream = new MemoryStream(data);
        using var br = new BinaryReader(stream);
        
        stream.Seek(-28, SeekOrigin.End);
        
        toc_hash = br.ReadBytes(8);
        version = br.ReadByte(); if (version != 1) throw new InvalidDataException("ParseIndex -> version");
        _11 = br.ReadByte(); if (_11 != 0) throw new InvalidDataException("ParseIndex -> unk1");
        _12 = br.ReadByte(); if (_12 != 0) throw new InvalidDataException("ParseIndex -> unk2");
        blockSizeKb = br.ReadByte(); if (blockSizeKb != 4) throw new InvalidDataException("ParseIndex -> blockSizeKb");
        offsetBytes = br.ReadByte(); // Normally 4 for archive indices, 5 for patch group indices, 6 for group indices, and 0 for loose file indices
        sizeBytes = br.ReadByte(); if (sizeBytes != 4) throw new InvalidDataException("ParseIndex -> sizeBytes");
        keySizeBytes = br.ReadByte(); if (keySizeBytes != 16) throw new InvalidDataException("ParseIndex -> keySizeBytes");
        checksumSize = br.ReadByte(); if (checksumSize != 8) throw new InvalidDataException("ParseIndex -> hashSize");
        numElements = br.ReadInt32(); if (numElements * (keySizeBytes + sizeBytes + offsetBytes) > stream.Length) throw new Exception("ParseIndex failed");
        footerChecksum = br.ReadBytes(checksumSize);
        
        stream.Seek(0, SeekOrigin.Begin);
        
        var indexBlockSize = 1024 * blockSizeKb;
        var recordSize = keySizeBytes + sizeBytes + offsetBytes;
        var recordsPerBlock = indexBlockSize / recordSize;
        var recordsRead = 0;
        
        while (recordsRead != numElements)
        {
            var blockRecordsRead = 0;

            for (var blockIndex = 0; blockIndex < recordsPerBlock && recordsRead < numElements; blockIndex++, recordsRead++)
            {
                var headerHash = new Hash(br.ReadBytes(keySizeBytes));
                var entry = new IndexEntry(br, sizeBytes);
                
                Entries.Add(headerHash, entry);

                blockRecordsRead++;
            }

            br.ReadBytes(indexBlockSize - (blockRecordsRead * recordSize));
        }
    }
    
    public struct IndexEntry
    {
        public uint size;
        
        public IndexEntry(BinaryReader br, byte sizeBytes)
        {
            if (sizeBytes == 4)
            {
                size = br.ReadUInt32(true);
            }
            else
            {
                throw new NotImplementedException("Index size reading other than 4 is not implemented!");
            }
        }
    }
    
    public static async Task<FileIndex[]> GetDataIndexes(Hash[] keys, CDN cdn, string pathType, string data_dir)
    {
        var indexes = new FileIndex[keys.Length];
        var tasks = new Task<FileIndex>[keys.Length];
        
        for (var i = 0; i < keys.Length; i++)
        {
            var key = keys[i];
            tasks[i] = GetDataIndex(cdn, key, pathType, data_dir);
        }

        await Task.WhenAll(tasks);

        for (var i = 0; i < keys.Length; i++)
        {
            indexes[i] = tasks[i].Result;
        }

        return indexes;
    }
    
    public static async Task<FileIndex> GetDataIndex(CDN? cdn, Hash? key, string pathType, string data_dir)
    {
        if (cdn == null || key == null)
            return new FileIndex([]);
        
        if (key?.KeyString == null)
            return new FileIndex([]);
        
        var saveDir = Path.Combine(data_dir, "indices");
        var savePath = Path.Combine(saveDir, key + ".index");
        
        if (File.Exists(savePath))
        {
            return new FileIndex(await File.ReadAllBytesAsync(savePath));
        }
        else
        {
            var hosts = cdn?.Hosts;
            if (hosts == null)
                return new FileIndex([]);
            
            foreach (var cdnURL in hosts)
            {
                var url = $@"http://{cdnURL}/{cdn?.Path}/{pathType}/{key?.UrlString}.index";
                var data = await Utils.GetDataFromURL(url);
                if (data == null) continue;
                if (ArmadilloCrypt.Instance != null)
                {
                    // Determine if the file is encrypted or not
                    using var stream = new MemoryStream(data);
                    using var br = new BinaryReader(stream);

                    stream.Seek(-20, SeekOrigin.End);

                    var encrypted = false;
                    var version = br.ReadByte();
                    if (version != 1) encrypted = true;
                    var unk1 = br.ReadByte();
                    if (unk1 != 0) encrypted = true;
                    var unk2 = br.ReadByte();
                    if (unk2 != 0) encrypted = true;
                    var blockSizeKb = br.ReadByte();
                    if (blockSizeKb != 4) encrypted = true;

                    if (encrypted)
                    {
                        data = ArmadilloCrypt.Instance.DecryptData(key, data);
                    }
                }
                
                if (!Directory.Exists(saveDir))
                    Directory.CreateDirectory(saveDir);
                await File.WriteAllBytesAsync(savePath, data);

                return new FileIndex(data);
            }
        }

        return new FileIndex([]);
    }
}