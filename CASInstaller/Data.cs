using System.Collections.Concurrent;
using Spectre.Console;

namespace CASInstaller;

public class Data
{
    const string cache_dir = "cache";
    static readonly ConcurrentDictionary<string, SemaphoreSlim> _fileLocks = new();
    const int DATA_TOTAL_SIZE_MAXIMUM = 1 << 30; // 1 GiB (0x40000000)
    public readonly int ID;
    readonly MemoryStream stream;
    readonly BinaryWriter writer;

    public Data(int ID, out byte[][] segmentHeaderKeys, string baseDir = "World of Warcraft")
    {
        this.ID = ID;

        // Initialize a new data file
        stream = new MemoryStream();

        writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);

        segmentHeaderKeys = casReconstructionHeaderSerialize(ID, baseDir);  // Write the initial header
    }

    public long Offset => stream.Length;

    public bool CanWrite(long writeSize)
    {
        return Offset + writeSize <= DATA_TOTAL_SIZE_MAXIMUM;
    }

    byte[][] casReconstructionHeaderSerialize(int dataNumber, string baseDir)
    {
        var container = new CasContainerIndex()
        {
            BaseDir = baseDir,
            BindMode = true,
            MaxSize = 30
        };

        var (_, SegmentHeaderKeys, _) = container.GenerateSegmentHeaderKeys((uint)dataNumber);

        // Binary shows segment headers are contiguous at 30 bytes each, no padding
        for (var i = 0; i < 16; i++)
        {
            var reversedSegmentHeaderKey = SegmentHeaderKeys[i].Reverse().ToArray();

            var header = new CasReconstructionHeader()
            {
                BLTEHash = reversedSegmentHeaderKey,
                size = 30,
                channel = casIndexChannel.Meta,
            };
            header.Write(writer, (ushort)dataNumber, (uint)(i * 30));
        }

        return SegmentHeaderKeys;
    }

    public async Task<uint> WriteDataEntry(IDX.Entry idxEntry, CDN? cdn, CDNConfig? cdnConfig, ConcurrentDictionary<Hash, ArchiveIndex.IndexEntry>? archiveGroup, bool truncateKey = false)
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

        if (data == null) return 0;

        // Use actual data length for the reconstruction header size
        var actualSize = (uint)(data.Length + 30);

        var reversedKey = key.Key.Reverse().ToArray();
        if (truncateKey)
        {
            // Agent stores only 9 bytes of eKey in reconstruction headers for download entries.
            // Zero reversed positions 0-6 (original key bytes 15-9).
            for (int i = 0; i < 7; i++)
                reversedKey[i] = 0;
        }

        var header = new CasReconstructionHeader()
        {
            BLTEHash = reversedKey,
            size = actualSize,
            channel = casIndexChannel.Data,
        };

        writer.Seek(offset, SeekOrigin.Begin);
        header.Write(writer, (ushort)archiveID, (uint)offset);
        writer.Write(data);

        // Pad to reserved size (manifest_size + 30) to match Agent.exe behavior.
        // Agent allocates space based on manifest size; if actual data is smaller,
        // the gap remains as unused space. This ensures correct entry offsets.
        var reservedEnd = offset + (long)idxEntry.Size;
        if (stream.Length < reservedEnd)
            stream.SetLength(reservedEnd);

        return actualSize;
    }

    public static async Task PreDownloadToCache(Hash key, CDN? cdn, CDNConfig? cdnConfig,
        ConcurrentDictionary<Hash, ArchiveIndex.IndexEntry>? archiveGroup)
    {
        if (archiveGroup != null && archiveGroup.TryGetValue(key, out var indexEntry))
            await DownloadFileFromIndex(indexEntry, cdn, cdnConfig);
        else
            await DownloadFileDirectly(key, cdn);
    }

    public static async Task<byte[]?> DownloadFileFromIndex(ArchiveIndex.IndexEntry indexEntry, CDN? cdn, CDNConfig? cdnConfig)
    {
        var hosts = cdn?.Hosts;
        if (hosts == null) return null;
        var archive = cdnConfig?.Archives?[indexEntry.archiveIndex];
        if (archive == null) return null;

        var dataFilePath = Path.Combine(cache_dir, $"{archive.Value.KeyString!}_{indexEntry.offset}_{indexEntry.size}.data");
        var fileLock = _fileLocks.GetOrAdd(dataFilePath, _ => new SemaphoreSlim(1, 1));
        await fileLock.WaitAsync();
        try
        {
            if (File.Exists(dataFilePath))
            {
                return await File.ReadAllBytesAsync(dataFilePath);
            }
            else
            {
                var decryptedData = await cdn.GetData(archive.Value, (int)indexEntry.offset, (int)indexEntry.size);

                // Cache
                await File.WriteAllBytesAsync(dataFilePath, decryptedData);

                return decryptedData;
            }
        }
        finally
        {
            fileLock.Release();
        }
    }

    public static async Task<byte[]?> DownloadFileDirectly(Hash key, CDN? cdn)
    {
        var dataFilePath = Path.Combine(cache_dir, $"{key.KeyString!}.data");
        var fileLock = _fileLocks.GetOrAdd(dataFilePath, _ => new SemaphoreSlim(1, 1));
        await fileLock.WaitAsync();
        try
        {
            if (File.Exists(dataFilePath))
            {
                return await File.ReadAllBytesAsync(dataFilePath);
            }
            else
            {
                var decryptedData = await cdn.GetData(key);

                // Cache
                await File.WriteAllBytesAsync(dataFilePath, decryptedData);

                return decryptedData;
            }
        }
        finally
        {
            fileLock.Release();
        }
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

    public void Finalize(string gameDataDir)
    {
        var dataPath = Path.Combine(gameDataDir, $"data.{ID:D3}");
        var fileStream = File.Create(dataPath);
        stream.WriteTo(fileStream);
        writer.Dispose();
        fileStream.Close();
        stream.SetLength(0);
        stream.Close();
        stream.Dispose();
    }
}
