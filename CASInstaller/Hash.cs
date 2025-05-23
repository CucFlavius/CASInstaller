﻿namespace CASInstaller;

public readonly struct Hash : IEquatable<Hash>, IComparable<Hash>
{
    public byte[] Key { get; }
    public string? KeyString => Key != null ? Convert.ToHexStringLower(Key) : null;
    public string UrlString => $"{KeyString?[..2]}/{KeyString?[2..4]}/{KeyString}";

    public Hash(byte[] key, bool key9 = false)
    {
        if (key.Length != 16)
            throw new ArgumentException("Hash key must be 16 bytes long.");

        if (key9)
        {
            Key = new byte[16];
            Array.Copy(key, Key, 9);
        }
        else
            Key = key;
    }

    public Hash(BinaryReader br)
    {
        Key = br.ReadBytes(16);
    }

    public Hash(string? hexKey)
    {
        Key = new byte[hexKey.Length / 2];
        for (var i = 0; i < hexKey.Length; i += 2)
        {
            Key[i / 2] = byte.Parse(hexKey.Substring(i, 2), System.Globalization.NumberStyles.HexNumber);
        }
    }

    public bool Equals(Hash other)
    {
        // Compare the full Key for equality
        return Key.AsSpan().SequenceEqual(other.Key);
    }

    public bool EqualsPartial(Hash other)
    {
        // Check the first 8 bytes (low part) and the least significant byte of the last byte (high part)
        return Key.AsSpan(0, 8).SequenceEqual(other.Key.AsSpan(0, 8)) &&
               (Key[15] & 0xFF) == (other.Key[15] & 0xFF);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(BitConverter.ToInt64(Key, 0), Key[15] & 0xFF);
    }

    public override string? ToString()
    {
        return KeyString;
    }

    public int CompareTo(Hash other)
    {
        return new ReadOnlySpan<byte>(Key).SequenceCompareTo(other.Key);
    }

    public override bool Equals(object? obj)
    {
        return obj is Hash other && Equals(other);
    }

    public bool EqualsTo(byte[] blockHash)
    {
        return Key.AsSpan().SequenceEqual(blockHash);
    }

    public bool IsEmpty()
    {
        return Key.AsSpan().SequenceEqual(new byte[16]);
    }
}
