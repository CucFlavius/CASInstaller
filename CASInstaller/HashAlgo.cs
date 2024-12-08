namespace CASInstaller;

using System;

using System;

public class HashAlgo
{
    // Table is extracted from Agent.exe 8020. Hasn't changed for quite a while.
    private static readonly uint[] TABLE_16C57A8 = new uint[]
    {
        0x049396b8, 0x72a82a9b, 0xee626cca, 0x9917754f, 0x15de40b1, 0xf5a8a9b6, 0x421eac7e, 0xa9d55c9a,
        0x317fd40c, 0x04faf80d, 0x3d6be971, 0x52933cfd, 0x27f64b7d, 0xc6f5c11b, 0xd5757e3a, 0x6c388745
    };

    // Method to calculate the checksum.
    // Arguments:
    //   header: Array containing the header data (must be at least 0x1e bytes).
    //   archiveIndex: Number of the data file the record is stored in (e.g. xxx in data.xxx).
    //   archiveOffset: Offset of the header inside the archive file.
    // Preconditions: header is at least 0x1e bytes.
    // Assumption: Code is written assuming little-endian.
    public static uint CalculateChecksum(byte[] header, ushort archiveIndex, uint archiveOffset)
    {
        if (header == null || header.Length < 0x1e)
        {
            //throw new ArgumentException("Header must be at least 0x1e bytes long.", nameof(header));
        }

        // Top two bits of the offset must be set to the bottom two bits of the archive index.
        uint offset = (archiveOffset & 0x3fffffff) | ((archiveIndex & 3u) << 30);

        uint encodedOffset = TABLE_16C57A8[(offset + 0x1e) & 0xf] ^ (offset + 0x1e);

        uint hashedHeader = 0;
        for (int i = 0; i < 0x1a; i++) // Offset of checksum_b in header.
        {
            int index = (i + (int)offset) & 3;
            hashedHeader ^= (uint)(header[i] << (index * 8));
        }

        uint checksumB = 0;
        for (int j = 0; j < 4; j++)
        {
            int i = j + 0x1a + (int)offset;
            byte hashedByte = (byte)((hashedHeader >> ((i & 3) * 8)) & 0xff);
            byte encodedByte = (byte)((encodedOffset >> ((i & 3) * 8)) & 0xff);
            checksumB |= (uint)((hashedByte ^ encodedByte) << (j * 8));
        }

        return checksumB;
    }

    // Internal state for the hash function
    private const uint InitVal = 0xDEADBEEF;

    public static uint HashLittle(byte[] key, int length, uint initval)
    {
        uint a, b, c;
        a = b = c = InitVal + (uint)length + initval;

        int i = 0;
        while (length > 12)
        {
            a += BitConverter.ToUInt32(key, i);
            b += BitConverter.ToUInt32(key, i + 4);
            c += BitConverter.ToUInt32(key, i + 8);
            Mix(ref a, ref b, ref c);
            length -= 12;
            i += 12;
        }

        switch (length)
        {
            case 12: c += BitConverter.ToUInt32(key, i + 8); b += BitConverter.ToUInt32(key, i + 4); a += BitConverter.ToUInt32(key, i); break;
            case 11: c += (uint)key[i + 10] << 16; goto case 10;
            case 10: c += (uint)key[i + 9] << 8; goto case 9;
            case 9: c += key[i + 8]; goto case 8;
            case 8: b += BitConverter.ToUInt32(key, i + 4); a += BitConverter.ToUInt32(key, i); break;
            case 7: b += (uint)key[i + 6] << 16; goto case 6;
            case 6: b += (uint)key[i + 5] << 8; goto case 5;
            case 5: b += key[i + 4]; goto case 4;
            case 4: a += BitConverter.ToUInt32(key, i); break;
            case 3: a += (uint)key[i + 2] << 16; goto case 2;
            case 2: a += (uint)key[i + 1] << 8; goto case 1;
            case 1: a += key[i]; break;
            case 0: return c;
        }

        Final(ref a, ref b, ref c);
        return c;
    }
    
    // Method to calculate two 32-bit hash values.
    // Arguments:
    //   key: The input key as a byte array.
    //   length: The length of the input key.
    //   pc: Initial value for primary hash, updated to final primary hash.
    //   pb: Initial value for secondary hash, updated to final secondary hash.
    public static void HashLittle2(byte[] key, int length, ref uint pc, ref uint pb)
    {
        uint a, b, c;
        a = b = c = 0xdeadbeef + (uint)length + pc;
        c += pb;

        int i = 0;
        while (length > 12)
        {
            a += BitConverter.ToUInt32(key, i);
            b += BitConverter.ToUInt32(key, i + 4);
            c += BitConverter.ToUInt32(key, i + 8);
            Mix(ref a, ref b, ref c);
            length -= 12;
            i += 12;
        }

        switch (length)
        {
            case 12: c += BitConverter.ToUInt32(key, i + 8); goto case 8;
            case 11: c += (uint)key[i + 10] << 16; goto case 10;
            case 10: c += (uint)key[i + 9] << 8; goto case 9;
            case 9: c += key[i + 8]; goto case 8;
            case 8: b += BitConverter.ToUInt32(key, i + 4); goto case 4;
            case 7: b += (uint)key[i + 6] << 16; goto case 6;
            case 6: b += (uint)key[i + 5] << 8; goto case 5;
            case 5: b += key[i + 4]; goto case 4;
            case 4: a += BitConverter.ToUInt32(key, i); break;
            case 3: a += (uint)key[i + 2] << 16; goto case 2;
            case 2: a += (uint)key[i + 1] << 8; goto case 1;
            case 1: a += key[i]; break;
            case 0: pc = c; pb = b; return;
        }

        Final(ref a, ref b, ref c);
        pc = c;
        pb = b;
    }

    // Mix function for internal use.
    private static void Mix(ref uint a, ref uint b, ref uint c)
    {
        a -= c; a ^= Rot(c, 4); c += b;
        b -= a; b ^= Rot(a, 6); a += c;
        c -= b; c ^= Rot(b, 8); b += a;
        a -= c; a ^= Rot(c, 16); c += b;
        b -= a; b ^= Rot(a, 19); a += c;
        c -= b; c ^= Rot(b, 4); b += a;
    }

    // Final function for internal use.
    private static void Final(ref uint a, ref uint b, ref uint c)
    {
        c ^= b; c -= Rot(b, 14);
        a ^= c; a -= Rot(c, 11);
        b ^= a; b -= Rot(a, 25);
        c ^= b; c -= Rot(b, 16);
        a ^= c; a -= Rot(c, 4);
        b ^= a; b -= Rot(a, 14);
        c ^= b; c -= Rot(b, 24);
    }

    // Helper function to perform a left rotation.
    private static uint Rot(uint x, int k)
    {
        return (x << k) | (x >> (32 - k));
    }
}