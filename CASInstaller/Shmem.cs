using System.Text;

namespace CASInstaller;

/// <summary>
/// Writes the CASC container shared memory file (shmem).
/// This file tracks per-bucket IDX version numbers and container state.
/// </summary>
public static class Shmem
{
    private const int SHMEM_SIZE = 20480; // 0x5000
    private const uint VERSION = 5;
    private const uint STRUCT_SIZE = 0x154; // 340
    private const uint FIELD_108 = 0x2AB8; // 10936 - appears constant across all shmem files
    private const uint FIELD_10C = 0x1000; // 4096 - block size
    private const uint FIELD_154 = 1;
    private const uint FIELD_158 = 0x228; // 552
    private const ulong FIELD_1D8 = 0x4000; // 16384

    public static void Write(string dir, string normalizedPath, uint[] bucketVersions, uint dataFileSeq)
    {
        if (!Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        var path = Path.Combine(dir, "shmem");
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var bw = new BinaryWriter(fs);

        // Header
        bw.Write(VERSION);          // 0x000
        bw.Write(STRUCT_SIZE);      // 0x004

        // Global name: "Global\{normalizedPath}" padded to 256 bytes
        var globalName = $"Global\\{normalizedPath}";
        var nameBytes = System.Text.Encoding.UTF8.GetBytes(globalName);
        var nameBuf = new byte[256];
        Array.Copy(nameBytes, nameBuf, Math.Min(nameBytes.Length, 255));
        bw.Write(nameBuf);          // 0x008-0x107

        // Fields at 0x108
        bw.Write(FIELD_108);        // 0x108
        bw.Write(FIELD_10C);        // 0x10C

        // Per-bucket IDX version numbers (16 uint32s)
        for (var i = 0; i < 16; i++)
            bw.Write(i < bucketVersions.Length ? bucketVersions[i] : 1u);   // 0x110-0x14F

        // Data file sequence and flags
        bw.Write(dataFileSeq);      // 0x150
        bw.Write(FIELD_154);        // 0x154
        bw.Write(FIELD_158);        // 0x158

        // Padding to 0x1D8
        var padTo1D8 = 0x1D8 - (int)fs.Position;
        if (padTo1D8 > 0)
            bw.Write(new byte[padTo1D8]);

        // Field at 0x1D8
        bw.Write(FIELD_1D8);        // 0x1D8

        // Pad to total size
        var remaining = SHMEM_SIZE - (int)fs.Position;
        if (remaining > 0)
            bw.Write(new byte[remaining]);
    }
}
