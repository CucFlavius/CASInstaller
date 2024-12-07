using System.Security.Cryptography;
using System.Text;
using Spectre.Console;

namespace CASInstaller;

public static class Extensions
{
    public static string ReadFourCC(this BinaryReader br)
    {
        string str = "";
        for (int i = 1; i <= 4; i++)
        {
            int b = br.ReadByte();
            try
            {
                var s = System.Convert.ToChar(b);
                if (s != '\0')
                {
                    str = s + str;
                }
            }
            catch
            {
                AnsiConsole.WriteLine("Couldn't convert Byte to Char: " + b);
            }
        }
        return str;
    }

    public static double ReadDouble(this BinaryReader reader, bool invertEndian = false)
    {
        if (invertEndian)
        {
            return BitConverter.ToDouble(reader.ReadInvertedBytes(8), 0);
        }

        return reader.ReadDouble();
    }

    public static Int16 ReadInt16(this BinaryReader reader, bool invertEndian = false)
    {
        if (invertEndian)
        {
            return BitConverter.ToInt16(reader.ReadInvertedBytes(2), 0);
        }

        return reader.ReadInt16();
    }

    public static Int32 ReadInt32(this BinaryReader reader, bool invertEndian = false)
    {
        if (invertEndian)
        {
            return BitConverter.ToInt32(reader.ReadInvertedBytes(4), 0);
        }

        return reader.ReadInt32();
    }

    public static Int64 ReadInt64(this BinaryReader reader, bool invertEndian = false)
    {
        if (invertEndian)
        {
            return BitConverter.ToInt64(reader.ReadInvertedBytes(8), 0);
        }

        return reader.ReadInt64();
    }

    public static Single ReadSingle(this BinaryReader reader, bool invertEndian = false)
    {
        if (invertEndian)
        {
            return BitConverter.ToSingle(reader.ReadInvertedBytes(4), 0);
        }

        return reader.ReadSingle();
    }

    public static UInt16 ReadUInt16(this BinaryReader reader, bool invertEndian = false)
    {
        if (invertEndian)
        {
            return BitConverter.ToUInt16(reader.ReadInvertedBytes(2), 0);
        }

        return reader.ReadUInt16();
    }

    public static UInt32 ReadUInt32(this BinaryReader reader, bool invertEndian = false)
    {
        if (invertEndian)
        {
            return BitConverter.ToUInt32(reader.ReadInvertedBytes(4), 0);
        }

        return reader.ReadUInt32();
    }

    public static UInt64 ReadUInt64(this BinaryReader reader, bool invertEndian = false)
    {
        if (invertEndian)
        {
            return BitConverter.ToUInt64(reader.ReadInvertedBytes(8), 0);
        }

        return reader.ReadUInt64();
    }

    public static UInt64 ReadUInt40(this BinaryReader reader, bool invertEndian = false)
    {
        ulong b1 = reader.ReadByte();
        ulong b2 = reader.ReadByte();
        ulong b3 = reader.ReadByte();
        ulong b4 = reader.ReadByte();
        ulong b5 = reader.ReadByte();

        if (invertEndian)
        {
            return (ulong)(b1 << 32 | b2 << 24 | b3 << 16 | b4 << 8 | b5);
        }
        else
        {
            return (ulong)(b5 << 32 | b4 << 24 | b3 << 16 | b2 << 8 | b1);
        }
    }

    private static byte[] ReadInvertedBytes(this BinaryReader reader, int byteCount)
    {
        byte[] byteArray = reader.ReadBytes(byteCount);
        Array.Reverse(byteArray);

        return byteArray;
    }
    
    public static int ReadInt32BE(this BinaryReader reader)
    {
        int val = reader.ReadInt32();
        int ret = (val >> 24 & 0xFF) << 0;
        ret |= (val >> 16 & 0xFF) << 8;
        ret |= (val >> 8 & 0xFF) << 16;
        ret |= (val >> 0 & 0xFF) << 24;
        return ret;
    }

    public static long ReadInt40BE(this BinaryReader reader)
    {
        byte[] val = reader.ReadBytes(5);
        return val[4] | val[3] << 8 | val[2] << 16 | val[1] << 24 | val[0] << 32;
    }

    public static void Skip(this BinaryReader reader, int bytes)
    {
        reader.BaseStream.Position += bytes;
    }

    public static ushort ReadUInt16BE(this BinaryReader reader)
    {
        byte[] val = reader.ReadBytes(2);
        return (ushort)(val[1] | val[0] << 8);
    }
    
    public static short ReadInt16BE(this BinaryReader reader)
    {
        byte[] val = reader.ReadBytes(2);
        return (short)(val[1] | val[0] << 8);
    }
    
    private static readonly MD5 md5 = MD5.Create();
    
    public static ReadOnlySpan<byte> ComputeMD5(this Stream stream) => md5.ComputeHash(stream);
    
    public static string ToHexString(this byte[] data)
    {
#if NET6_0_OR_GREATER
        return Convert.ToHexString(data);
#else
            if (data == null)
                throw new ArgumentNullException(nameof(data));
            if (data.Length == 0)
                return string.Empty;
            if (data.Length > int.MaxValue / 2)
                throw new ArgumentOutOfRangeException(nameof(data), "SR.ArgumentOutOfRange_InputTooLarge");
            return HexConverter.ToString(data, HexConverter.Casing.Upper);
#endif
    }
    
    public static string ToHexString(this ReadOnlySpan<byte> bytes, bool lower = false)
    {
        Span<char> c = stackalloc char[bytes.Length * 2];
        int b;
        for (int i = 0; i < bytes.Length; i++)
        {
            b = bytes[i] >> 4;
            c[i * 2] = lower ? (char)(87 + b + (((b - 10) >> 31) & -39)) : (char)(55 + b + (((b - 10) >> 31) & -7));
            b = bytes[i] & 0xF;
            c[i * 2 + 1] = lower ? (char)(87 + b + (((b - 10) >> 31) & -39)) : (char)(55 + b + (((b - 10) >> 31) & -7));
        }
        return new string(c);
    }
    
    public static byte[] FromHexString(this string str)
    {
        return Convert.FromHexString(str);
    }

    public static string CreateCdnUrl(this string str)
    {
        return $"{str[..2]}/{str[2..4]}/{str}";
    }
    
    public static MemoryStream CopyToMemoryStream(this Stream src)
    {
        var ms = new MemoryStream();
        src.CopyTo(ms);
        ms.Position = 0;
        return ms;
    }
    
    public static void CopyBytesFromPos(this Stream input, Stream output, int offset, int bytes)
    {
        var buffer = new byte[0x1000];
        var pos = 0;
        int read;
        while (pos < offset && (read = input.Read(buffer, 0, Math.Min(buffer.Length, offset - pos))) > 0)
        {
            pos += read;
        }
        while (bytes > 0 && (read = input.Read(buffer, 0, Math.Min(buffer.Length, bytes))) > 0)
        {
            output.Write(buffer, 0, read);
            bytes -= read;
        }
    }
    
    /// <summary> Reads the NULL terminated string from 
    /// the current stream and advances the current position of the stream by string length + 1.
    /// <seealso cref="BinaryReader.ReadString"/>
    /// </summary>
    public static string ReadCString(this BinaryReader reader)
    {
        return reader.ReadCString(System.Text.Encoding.UTF8);
    }

    /// <summary> Reads the NULL terminated string from 
    /// the current stream and advances the current position of the stream by string length + 1.
    /// <seealso cref="BinaryReader.ReadString"/>
    /// </summary>
    public static string ReadCString(this BinaryReader reader, System.Text.Encoding encoding)
    {
        var bytes = new List<byte>();
        byte b;
        while ((b = reader.ReadByte()) != 0)
            bytes.Add(b);
        return encoding.GetString(bytes.ToArray());
    }
    
    public static byte[] ToByteArray(this string? str)
    {
        str = str.Replace(" ", string.Empty);
        return Convert.FromHexString(str);
    }
}