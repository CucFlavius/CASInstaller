using System.Collections.Concurrent;
using Spectre.Console;

namespace CASInstaller;

public class Data
{
    const string cache_dir = "cache";
    private static readonly SemaphoreSlim[] _stripedLocks = Enumerable.Range(0, 256)
        .Select(_ => new SemaphoreSlim(1, 1)).ToArray();
    const int DATA_TOTAL_SIZE_MAXIMUM = 1 << 30; // 1 GiB (0x40000000)
    public readonly int ID;
    readonly FileStream _stream;
    readonly BinaryWriter _writer;

    public Data(int ID, out byte[][] segmentHeaderKeys, string baseDir, string dataDir)
    {
        this.ID = ID;

        var dataPath = Path.Combine(dataDir, $"data.{ID:D3}");
        _stream = new FileStream(dataPath, FileMode.Create, FileAccess.Write, FileShare.Read, 65536);
        _writer = new BinaryWriter(_stream, System.Text.Encoding.UTF8, leaveOpen: true);

        segmentHeaderKeys = casReconstructionHeaderSerialize(ID, baseDir);
    }

    public long Offset => _stream.Length;

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
            header.Write(_writer, (ushort)dataNumber, (uint)(i * 30));
        }

        return SegmentHeaderKeys;
    }

    /// <summary>
    /// Write an entry using pre-downloaded data (no network I/O).
    /// </summary>
    public uint WriteDataEntry(IDX.Entry idxEntry, byte[] data, bool truncateKey = false)
    {
        var offset = idxEntry.Offset;
        var key = idxEntry.Key;
        var archiveID = idxEntry.ArchiveID;

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

        _writer.Seek(offset, SeekOrigin.Begin);
        header.Write(_writer, (ushort)archiveID, (uint)offset);
        _writer.Write(data);

        // Pad to reserved size (manifest_size + 30) to match Agent.exe behavior.
        // Agent allocates space based on manifest size; if actual data is smaller,
        // the gap remains as unused space. This ensures correct entry offsets.
        var reservedEnd = offset + (long)idxEntry.Size;
        if (_stream.Length < reservedEnd)
            _stream.SetLength(reservedEnd);

        return actualSize;
    }

    /// <summary>
    /// Write an entry, downloading data from CDN/cache if needed.
    /// </summary>
    public async Task<uint> WriteDataEntry(IDX.Entry idxEntry, CDN? cdn, CDNConfig? cdnConfig, ConcurrentDictionary<Hash, ArchiveIndex.IndexEntry>? archiveGroup, bool truncateKey = false)
    {
        var key = idxEntry.Key;

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

        return WriteDataEntry(idxEntry, data, truncateKey);
    }

    private static SemaphoreSlim GetStripedLock(string key)
    {
        return _stripedLocks[(uint)key.GetHashCode() & 0xFF];
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
        var fileLock = GetStripedLock(dataFilePath);
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
                if (decryptedData == null) return null;

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
        var fileLock = GetStripedLock(dataFilePath);
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
                if (decryptedData == null) return null;

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

    public void FinalizeAndClose()
    {
        _writer.Flush();
        _stream.Flush();
        _writer.Dispose();
        _stream.Dispose();
    }
}
