using System.Collections.Concurrent;
using Spectre.Console;

namespace CASInstaller;

public class Data
{
    private const string cache_dir = "cache";
    private const int DATA_TOTAL_SIZE_MAXIMUM = 1023 * 1024 * 1024;
    public readonly int ID;
    private readonly MemoryStream stream;

    public Data(int ID, out byte[][] segmentHeaderKeys)
    {
        this.ID = ID;

        // Initialize a new data file
        stream = new MemoryStream();
        
        using var bw = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        
        segmentHeaderKeys = casReconstructionHeaderSerialize(bw, ID);  // Write the initial header
    }

    public long Offset => stream.Length;

    public bool CanWrite(long writeSize)
    {
        return Offset + writeSize <= DATA_TOTAL_SIZE_MAXIMUM;
    }

    private byte[][] casReconstructionHeaderSerialize(BinaryWriter bw, int dataNumber)
    {
        var container = new CasContainerIndex()
        {
            BaseDir = "World of Warcraft",
            BindMode = true,
            MaxSize = 30
        };

        var (_, SegmentHeaderKeys, _) = container.GenerateSegmentHeaderKeys((uint)dataNumber);
        
        for (var i = 0; i < 16; i++)
        {
            var reversedSegmentHeaderKey = SegmentHeaderKeys[i].Reverse().ToArray();
            
            var header = new CasReconstructionHeader()
            {
                BLTEHash = reversedSegmentHeaderKey,
                size = 30,
                channel = casIndexChannel.Meta,
            };
            //header.Write(bw, (ushort)dataNumber, (uint)(i * 30));
            bw.Write(new byte[30]);
        }

        return SegmentHeaderKeys;
    }
    
    public async Task WriteDataEntry(IDX.Entry idxEntry, CDN? cdn, CDNConfig? cdnConfig, ConcurrentDictionary<Hash, ArchiveIndex.IndexEntry>? archiveGroup)
    {
        var offset = idxEntry.Offset;
        var key = idxEntry.Key;
        var archiveID = idxEntry.ArchiveID;

        byte[]? data = null;

        if (archiveGroup != null && archiveGroup.TryGetValue(key, out var indexEntry))
        {
            try
            {
                data = await DownloadFileFromIndex(indexEntry, cdn, cdnConfig);
            }
            catch (Exception e)
            {
                AnsiConsole.WriteException(e);
                throw;
            }
        }
        else
        {
            data = await DownloadFileDirectly(key, cdn);
        }

        if (data == null) return;

        // Ensure we're not overwriting stream position by reusing the BinaryWriter
        await using var bws = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        var header = new CasReconstructionHeader()
        {
            BLTEHash = key.Key.Reverse().ToArray(),
            size = idxEntry.Size,
            channel = casIndexChannel.Data,
        };
        
        bws.Seek(offset, SeekOrigin.Begin);
        header.Write(bws, (ushort)archiveID, (uint)offset);
        bws.Write(data);
    }
    
    public static async Task<byte[]?> DownloadFileFromIndex(ArchiveIndex.IndexEntry indexEntry, CDN? cdn, CDNConfig? cdnConfig)
    {
        var hosts = cdn?.Hosts;
        if (hosts == null) return null;
        var archive = cdnConfig?.Archives?[indexEntry.archiveIndex];
        if (archive == null) return null;
        
        foreach (var cdnURL in hosts)
        {
            var url = $@"http://{cdnURL}/{cdn?.Path}/data/{archive.Value.UrlString}";
            var dataFilePath = Path.Combine(cache_dir, $"{archive.Value.KeyString!}_{indexEntry.offset}_{indexEntry.size}.data");
            if (File.Exists(dataFilePath))
            {
                return await File.ReadAllBytesAsync(dataFilePath);
            }
            else
            {
                var encryptedData = await Utils.GetDataFromURL(url, (int)indexEntry.offset, (int)indexEntry.size);
                if (encryptedData == null)
                    continue;

                byte[]? decryptedData;
                if (ArmadilloCrypt.Instance == null)
                    decryptedData = encryptedData;
                else
                    decryptedData = ArmadilloCrypt.Instance?.DecryptData(archive.Value, encryptedData);

                if (decryptedData == null) continue;

                // Cache
                await File.WriteAllBytesAsync(dataFilePath, decryptedData);
                
                return decryptedData;
            }
        }

        return null;
    }
    
    public static async Task<byte[]?> DownloadFileDirectly(Hash key, CDN? cdn)
    {
        var hosts = cdn?.Hosts;
        if (hosts == null) return null;
        
        foreach (var cdnURL in hosts)
        {
            var url = $@"http://{cdnURL}/{cdn?.Path}/data/{key.UrlString}";
            var encryptedData = await Utils.GetDataFromURL(url);

            if (encryptedData == null)
                continue;

            byte[]? data;
            if (ArmadilloCrypt.Instance == null)
                data = encryptedData;
            else
                data = ArmadilloCrypt.Instance?.DecryptData(key, encryptedData);

            if (data == null) continue;
            
            return data;
        }
        
        return null;
    }
    
    public static byte cascGetBucketIndex(Hash key)
    {
        return cascGetBucketIndex(key.Key);
    }
    
    public static byte cascGetBucketIndex(byte[] k)
    {
        var i = k[0] ^ k[1] ^ k[2] ^ k[3] ^ k[4] ^ k[5] ^ k[6] ^ k[7] ^ k[8];
        return (byte)((i & 0xf) ^ (i >> 4));
    }
    
    public void Finalize(string gameDataDir, Dictionary<byte, IDX> idxMap)
    {
        var dataPath = Path.Combine(gameDataDir, $"data.{ID:D3}");
        var fileStream = File.Create(dataPath);
        stream.WriteTo(fileStream);
        fileStream.Close();
        stream.SetLength(0);
        stream.Close();
    }
}