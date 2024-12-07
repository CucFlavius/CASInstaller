using System.Collections.Concurrent;
using System.IO.MemoryMappedFiles;
using System.Security.Cryptography;
using Spectre.Console;

namespace CASInstaller;

public struct ArchiveIndex
{
    private const int ArchiveBlockSize = 4096;
    
    private class Footer
    {
        public readonly byte blockSizeKb;
        public readonly byte offsetBytes;
        public readonly byte sizeBytes;
        public readonly byte keySizeBytes;
        public readonly int numElements;
        
        public Footer(BinaryReader br)
        {
            var toc_hash = br.ReadBytes(8);
            var version = br.ReadByte(); if (version != 1) throw new InvalidDataException("ParseIndex -> version");
            var _11 = br.ReadByte(); if (_11 != 0) throw new InvalidDataException("ParseIndex -> unk1");
            var _12 = br.ReadByte(); if (_12 != 0) throw new InvalidDataException("ParseIndex -> unk2");
            blockSizeKb = br.ReadByte(); if (blockSizeKb != 4) throw new InvalidDataException("ParseIndex -> blockSizeKb");
            offsetBytes = br.ReadByte(); // Normally 4 for archive indices, 5 for patch group indices, 6 for group indices, and 0 for loose file indices
            sizeBytes = br.ReadByte(); if (sizeBytes != 4) throw new InvalidDataException("ParseIndex -> sizeBytes");
            keySizeBytes = br.ReadByte(); if (keySizeBytes != 16) throw new InvalidDataException("ParseIndex -> keySizeBytes");
            var checksumSize = br.ReadByte(); if (checksumSize != 8) throw new InvalidDataException("ParseIndex -> hashSize");
            numElements = br.ReadInt32(); if (numElements * (keySizeBytes + sizeBytes + offsetBytes) > br.BaseStream.Length) throw new Exception("ParseIndex failed");
            var footerChecksum = br.ReadBytes(checksumSize);
        }
    }

    private ArchiveIndex(byte[] data, ushort index, ConcurrentDictionary<Hash, IndexEntry>? archiveGroup)
    {
        if (data.Length == 0)
            return;
        
        if (archiveGroup == null)
            return;
        
        using var stream = new MemoryStream(data);
        using var br = new BinaryReader(stream);
        
        stream.Seek(-28, SeekOrigin.End);
        
        var footer = new Footer(br);
        
        stream.Seek(0, SeekOrigin.Begin);
        
        var indexBlockSize = 1024 * footer.blockSizeKb;
        var recordSize = footer.keySizeBytes + footer.sizeBytes + footer.offsetBytes;
        var recordsPerBlock = indexBlockSize / recordSize;
        var recordsRead = 0;
        
        while (recordsRead != footer.numElements)
        {
            var blockRecordsRead = 0;
            
            for (var blockIndex = 0; blockIndex < recordsPerBlock && recordsRead < footer.numElements; blockIndex++, recordsRead++)
            {
                var bytes = br.ReadBytes(footer.keySizeBytes);
                var headerHash = new Hash(bytes);
                var entry = new IndexEntry(br, footer.sizeBytes, footer.offsetBytes, index);

                archiveGroup.TryAdd(headerHash, entry);

                blockRecordsRead++;
            }

            br.BaseStream.Position += indexBlockSize - (blockRecordsRead * recordSize);
        }
    }
    
    public struct IndexEntry
    {
        public readonly uint size;
        public readonly uint offset;
        public readonly ushort archiveIndex;
        
        public IndexEntry(BinaryReader br, byte sizeBytes, byte offsetBytes, ushort index)
        {
            archiveIndex = index;
            
            if (sizeBytes == 4)
                size = br.ReadUInt32(true);
            else
                throw new NotImplementedException("Index size reading other than 4 is not implemented!");

            switch (offsetBytes)
            {
                case 4:
                    // Archive index
                    offset = br.ReadUInt32(true);
                    break;
                case 5:
                    // Patch group index
                    archiveIndex = br.ReadByte();
                    offset = br.ReadUInt32(true);
                    break;
                case 6:
                    // Archive group index
                    archiveIndex = br.ReadUInt16(true);
                    offset = br.ReadUInt32(true);
                    break;
                case 0:
                    // File index
                    offset = 0;
                    break;
                default:
                    throw new NotImplementedException("Offset size reading other than 4/6/0 is not implemented!");
            }
        }
    }
    
    public static async Task<ArchiveIndex> GetDataIndex(CDN? cdn, Hash? key, string pathType, string? data_dir,
        ConcurrentDictionary<Hash, IndexEntry>? archiveGroup, ushort index = 0)
    {
        if (cdn == null || key?.KeyString == null)
            return new ArchiveIndex([], index, archiveGroup);
        
        var saveDir = Path.Combine(data_dir ?? "", "indices");
        var savePath = Path.Combine(saveDir, key + ".index");
        
        if (File.Exists(savePath))
        {
            var data = await File.ReadAllBytesAsync(savePath);
            return new ArchiveIndex(data, index, archiveGroup);
        }
        else
        {
            var hosts = cdn?.Hosts;
            if (hosts == null)
                return new ArchiveIndex([], index, archiveGroup);
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

                if (data_dir != null)
                {
                    if (!Directory.Exists(saveDir))
                        Directory.CreateDirectory(saveDir);
                    await File.WriteAllBytesAsync(savePath, data);
                }

                return new ArchiveIndex(data, index, archiveGroup);
            }
        }

        return new ArchiveIndex([], index, archiveGroup);
    }
    
    public static void GenerateIndexGroupFile(ConcurrentDictionary<Hash, IndexEntry>? archiveGroup, string archiveGroupPath, byte offsetBytes)
    {
        AnsiConsole.WriteLine("Generating archive group index...");
        
        if (archiveGroup == null)
            return;
        
        var archiveKeys = new List<Hash>(archiveGroup.Keys);
        archiveKeys.Sort();

        using var fs = new FileStream(archiveGroupPath, FileMode.Create, FileAccess.Write);
        using var bw = new BinaryWriter(fs);

        // Buffer to hold the current block
        var blockBuffer = new MemoryStream();
        using var blockWriter = new BinaryWriter(blockBuffer);

        List<Hash> lastBlockEkeys = [];
        List<byte[]> md5s = [];
        
        Hash previousHash = default;
        foreach (var hash in archiveKeys)
        {
            // If the block buffer exceeds the block size, flush it to the file
            if (blockBuffer.Length + 26 >= ArchiveBlockSize)
            {
                lastBlockEkeys.Add(previousHash);
                var md5 = WriteBlockToFile(fs, blockBuffer, true);
                md5s.Add(md5);
            }
            
            var entry = archiveGroup[hash];

            // Write entry to block buffer
            blockWriter.Write(hash.Key);
            blockWriter.Write(entry.size, true);
            switch (offsetBytes)
            {
                case 5:
                    blockWriter.Write((byte)entry.archiveIndex);
                    break;
                case 6:
                    blockWriter.Write(entry.archiveIndex, true);
                    break;
            }
            blockWriter.Write(entry.offset, true);
            
            previousHash = hash;
        }

        lastBlockEkeys.Add(previousHash);
        
        // Write the final block and pad if necessary
        if (blockBuffer.Length > 0)
        {
            var md5 = WriteBlockToFile(fs, blockBuffer, true);
            md5s.Add(md5);
        }

        // Write the TOC
        // Write last block eKeys
        using var tocMS = new MemoryStream();
        using var tocBW = new BinaryWriter(tocMS);

        // Write keys to the TOC
        foreach (var key in lastBlockEkeys)
        {
            tocBW.Write(key.Key);
        }

        // Write the lower part of MD5 hashes
        foreach (var md5 in md5s)
        {
            tocBW.Write(md5, 0, 8);
        }

        // Calculate the MD5 hash of the TOC
        tocBW.Flush(); // Ensure all data is written to the MemoryStream
        tocMS.Position = 0; // Reset the stream position for hashing

        using var md5Hasher = MD5.Create();
        var tocMD5 = md5Hasher.ComputeHash(tocMS);
        
        // Optionally, save the TOC to a file if needed
        tocMS.Position = 0; // Reset position after hashing
        tocMS.CopyTo(fs);
        
        // Write the TOC MD5 hash to the file
        bw.Write(tocMD5, 0, 8);
        
        using var footerMS = new MemoryStream();
        using var footerBW = new BinaryWriter(footerMS);
        
        // Write version
        footerBW.Write((byte)1);
        
        // Write _11
        footerBW.Write((byte)0);
        
        // Write _12
        footerBW.Write((byte)0);
        
        // Write block size
        footerBW.Write((byte)4);
        
        // Write offset bytes
        footerBW.Write(offsetBytes);
        
        // Write size bytes
        footerBW.Write((byte)4);
        
        // Write key size bytes
        footerBW.Write((byte)16);
        
        // Write checksum size
        footerBW.Write((byte)8);
        
        // Write number of elements
        footerBW.Write(archiveKeys.Count);
        
        footerBW.Write(new byte[8]);
        
        footerBW.Flush(); // Ensure all data is written to the MemoryStream
        footerMS.Position = 0; // Reset the stream position for hashing
        
        var footerMD5 = md5Hasher.ComputeHash(footerMS);
        
        footerMS.Position = 0; // Reset position after hashing
        footerMS.CopyTo(fs);
        
        // Write the footer MD5 hash to the file
        bw.BaseStream.Position -= 8;
        bw.Write(footerMD5, 0, 8);
    }
    
    // Method to write a block to the file, pad if necessary, and return the MD5 hash
    private static byte[] WriteBlockToFile(Stream stream, MemoryStream blockBuffer, bool pad = false)
    {
        if (pad)
        {
            var padding = ArchiveBlockSize - blockBuffer.Length;
            if (padding > 0)
            {
                blockBuffer.Write(new byte[padding], 0, (int)padding);
            }
        }

        // Reset position to calculate MD5 and copy to stream
        blockBuffer.Position = 0;

        // Calculate MD5 hash
        using var md5 = MD5.Create();
        var hash = md5.ComputeHash(blockBuffer);

        // Write the block to the stream
        blockBuffer.Position = 0; // Reset position after hashing
        blockBuffer.CopyTo(stream);

        // Reset the block buffer for reuse
        blockBuffer.SetLength(0);

        // Return the MD5 hash
        return hash;
    }

}