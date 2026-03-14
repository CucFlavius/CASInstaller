namespace CASInstaller;

/// <summary>
/// V8 KMT (Key Mapping Table) IDX writer for product-specific loose container index.
/// Creates IDX files in the product-specific directory (e.g., Data/wow_classic_era_ptr/).
/// </summary>
public class IDXV8
{
    private const short VERSION = 8;
    private const byte FLAGS = 0;
    private const byte SIZE_SIZE = 8;
    private const byte OFFSET_SIZE = 8;
    private const byte KEY_SIZE = 16;
    private const byte RESERVED = 0x1E;
    private const int UPDATE_SECTION_ALIGN = 4096;
    private const int TOTAL_FILE_SIZE = 131072; // 128KB

    public readonly string Path;
    public readonly byte Bucket;

    public IDXV8(string path, byte bucket)
    {
        Path = path;
        Bucket = bucket;
    }

    public void Write()
    {
        using var fs = new FileStream(Path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
        using var bw = new BinaryWriter(fs);

        // Build file header data (16 bytes)
        using var headerMs = new MemoryStream();
        using var headerBw = new BinaryWriter(headerMs);
        headerBw.Write(VERSION);        // uint16
        headerBw.Write(Bucket);         // uint8
        headerBw.Write(FLAGS);          // uint8
        headerBw.Write(SIZE_SIZE);      // uint8
        headerBw.Write(OFFSET_SIZE);    // uint8
        headerBw.Write(KEY_SIZE);       // uint8
        headerBw.Write(RESERVED);       // uint8
        headerBw.Write(0xFFFFFFFF);     // seq_low (uint32)
        headerBw.Write(0xFFFFFFFF);     // seq_high (uint32)
        headerBw.Flush();
        var headerBytes = headerMs.ToArray();

        // Hash the header data
        uint pc = 0, pb = 0;
        HashAlgo.HashLittle2(headerBytes, headerBytes.Length, ref pc, ref pb);

        // Write file header section
        bw.Write((int)headerBytes.Length);   // section_size
        bw.Write(pc);                        // section_hash
        bw.Write(headerBytes);               // header data

        // Padding to 32-byte alignment
        var padTo32 = 32 - (int)fs.Position;
        if (padTo32 > 0)
            bw.Write(new byte[padTo32]);

        // Sorted section (empty)
        bw.Write(0);    // sorted_size = 0
        bw.Write(0u);   // sorted_hash = 0

        // Pad to total file size (update section is all zeros)
        var remaining = TOTAL_FILE_SIZE - (int)fs.Position;
        if (remaining > 0)
            bw.Write(new byte[remaining]);

        bw.Flush();
    }

    public static void WriteProductIndex(string productDir)
    {
        if (!Directory.Exists(productDir))
            Directory.CreateDirectory(productDir);

        for (var i = 0; i < 16; i++)
        {
            var bucket = (byte)i;
            var path = System.IO.Path.Combine(productDir, $"{bucket:x2}00000001.idx");
            var idx = new IDXV8(path, bucket);
            idx.Write();
        }
    }
}
