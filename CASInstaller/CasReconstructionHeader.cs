namespace CASInstaller;

public enum casIndexChannel : byte
{
    Data = 0x0,
    Meta = 0x1,
}

struct CasReconstructionHeader
{
    public byte[] BLTEHash;
    public uint size;
    public casIndexChannel channel;
    uint checksumA;
    uint checksumB;

    public void Write(BinaryWriter bw, ushort archiveIndex, uint archiveOffset)
    {
        using var headerMs = new MemoryStream();
        using var headerBw = new BinaryWriter(headerMs);
        headerBw.Write(BLTEHash);
        headerBw.Write(size);
        headerBw.Write((byte)channel);
        headerBw.Write((byte)0);

        headerBw.Flush();
        headerMs.Position = 0;
        checksumA = HashAlgo.HashLittle(headerMs.ToArray(), 0x16, 0x3D6BE971);
        headerMs.Position = 0x16;
        headerBw.Write(checksumA);

        headerBw.Flush();
        headerMs.Position = 0;
        checksumB = HashAlgo.CalculateChecksum(headerMs.ToArray(), archiveIndex, archiveOffset);
        headerMs.Position = 0x1A;
        headerBw.Write(checksumB);

        bw.Write(headerMs.ToArray());
    }
}
