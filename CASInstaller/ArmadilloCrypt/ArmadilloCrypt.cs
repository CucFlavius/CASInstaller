using System.Buffers;
using System.Security.Cryptography;

namespace CASInstaller;

public class ArmadilloCrypt
{
    public static ArmadilloCrypt? Instance => crypt;
    static ArmadilloCrypt? crypt;

    public byte[]? Key => _key;
    readonly byte[]? _key;

    public ArmadilloCrypt(byte[]? key)
    {
        _key = key;
    }

    public ArmadilloCrypt(string keyName)
    {
        if (!LoadKeyFile(keyName, out _key))
        {
            throw new ArgumentException("Invalid key name", nameof(keyName));
        }
    }

    static bool LoadKeyFile(string keyName, out byte[]? key)
    {
        var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

        var fi = new FileInfo(Path.Combine(appDataPath, "Battle.net", "Armadillo", keyName + ".ak"));

        key = null;

        if (!fi.Exists)
            return false;

        if (fi.Length != 20)
            return false;

        using (var file = fi.OpenRead())
        {
            var keyBytes = new byte[16];

            if (file.Read(keyBytes, 0, keyBytes.Length) != 16)
                return false;

            var checkSum = new byte[4];

            if (file.Read(checkSum, 0, checkSum.Length) != 4)
                return false;

            var keyMD5 = MD5.HashData(keyBytes);

            // check first 4 bytes
            for (var i = 0; i < checkSum.Length; i++)
            {
                if (checkSum[i] != keyMD5[i])
                    return false;
            }

            key = keyBytes;
        }

        return true;
    }

    public byte[] DecryptFile(string name)
    {
        using (var fs = new FileStream(name, FileMode.Open))
            return DecryptFile(name, fs);
    }

    public byte[] DecryptFile(string name, byte[] data)
    {
        using var ms = new MemoryStream(data);
        return DecryptFile(name, ms);
    }

    public byte[] DecryptFile(string filePath, Stream stream)
    {
        var fileName = Path.GetFileNameWithoutExtension(filePath);

        if (fileName.Length != 32)
            throw new ArgumentException("Invalid file name", nameof(filePath));

        var IV = fileName[16..].FromHexString();

        /*
        using (var decryptor = KeyService.SalsaInstance.CreateDecryptor(_key, IV))
        using (var cs = new CryptoStream(stream, decryptor, CryptoStreamMode.Read))
        using (var ms = cs.CopyToMemoryStream())
        {
            return ms.ToArray();
        }
        */
        // Use a single MemoryStream to avoid redundant allocations
        using var decryptor = KeyService.SalsaInstance.CreateDecryptor(_key, IV);
        using var cs = new CryptoStream(stream, decryptor, CryptoStreamMode.Read);
        // Use a pooled buffer for reading
        return ReadStreamToByteArray(cs);
    }

    public byte[] DecryptData(string? key, byte[] encryptedData)
    {
        var IV = key[16..].FromHexString();
        using var stream = new MemoryStream(encryptedData);
        using var decryptor = KeyService.SalsaInstance.CreateDecryptor(_key, IV);
        using var cs = new CryptoStream(stream, decryptor, CryptoStreamMode.Read);
        using var ms = cs.CopyToMemoryStream();
        return ms.ToArray(); 
    }

    [Obsolete]
    public byte[] DecryptData(Hash? key, byte[]? encryptedData)
    {
        if (key == null)
            throw new ArgumentNullException(nameof(key));
        
        if (encryptedData == null)
            throw new ArgumentNullException(nameof(encryptedData));
        
        var iv = key?.Key[8..];

        using var stream = new MemoryStream(encryptedData);
        using var decryptor = KeyService.SalsaInstance.CreateDecryptor(_key, iv);
        using var cs = new CryptoStream(stream, decryptor, CryptoStreamMode.Read);

        // Read and decrypt data directly into a byte array
        return ReadStreamToByteArray(cs);
    }

    public Stream DecryptFileToStream(string name, Stream stream)
    {
        var fileName = Path.GetFileNameWithoutExtension(name);

        if (fileName.Length != 32)
            throw new ArgumentException("Invalid file name", nameof(name));

        var IV = fileName[16..].FromHexString();

        using (var decryptor = KeyService.SalsaInstance.CreateDecryptor(_key, IV))
        using (var cs = new CryptoStream(stream, decryptor, CryptoStreamMode.Read))
        {
            return cs.CopyToMemoryStream();
        }
    }

    public Stream DecryptFileToStream(string name, Stream stream, int offset, int length)
    {
        var fileName = Path.GetFileNameWithoutExtension(name);

        if (fileName.Length != 32)
            throw new ArgumentException("Invalid file name", nameof(name));

        var IV = fileName[16..].FromHexString();

        if (offset != 0)
        {
            using (var fake = new MemoryStream(offset + length))
            {
                fake.Position = offset;
                stream.CopyTo(fake);
                fake.Position = 0;

                using (var decryptor = KeyService.SalsaInstance.CreateDecryptor(_key, IV))
                using (var cs = new CryptoStream(fake, decryptor, CryptoStreamMode.Read))
                {
                    var ms = new MemoryStream(length);
                    cs.CopyBytesFromPos(ms, offset, length);
                    ms.Position = 0;
                    return ms;
                }
            }
        }

        using (var decryptor = KeyService.SalsaInstance.CreateDecryptor(_key, IV))
        using (var cs = new CryptoStream(stream, decryptor, CryptoStreamMode.Read))
        {
            return cs.CopyToMemoryStream();
        }
    }

    public static void Init(string keyName)
    {
        crypt = new(keyName);
    }
    
    private static byte[] ReadStreamToByteArray(Stream stream)
    {
        using (var ms = new MemoryStream())
        {
            byte[] buffer = ArrayPool<byte>.Shared.Rent(81920); // Reuse a shared buffer
            try
            {
                int bytesRead;
                while ((bytesRead = stream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    ms.Write(buffer, 0, bytesRead);
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer); // Return the buffer to the pool
            }
            return ms.ToArray();
        }
    }
}