using System.Collections;

namespace CASInstaller;

public struct TagInfo
{
    public readonly string name;
    public readonly ushort type;
    public readonly BitArray bitmap;

    public TagInfo(BinaryReader br, int bytesPerTag)
    {
        name = br.ReadCString();
        type = br.ReadUInt16(true);

        var fileBits = br.ReadBytes(bytesPerTag);

        for (var j = 0; j < bytesPerTag; j++)
            fileBits[j] = (byte)((fileBits[j] * 0x0202020202 & 0x010884422010) % 1023);

        bitmap = new BitArray(fileBits);
    }
}