using System.Security.Cryptography;
using System.Text;
using Spectre.Console;

public class CasContainerIndex
{
    public enum CasResult
    {
        Success = 0,
        InvalidContainerSize = 1,
        ReconstructionModeError = 8
    }
    
    public string BaseDir { get; set; }
    public ulong MaxSize { get; set; }
    public bool BindMode { get; set; } // Assuming a boolean for simplicity; adapt as needed.

    public (CasResult Result, byte[][] SegmentHeaderKeys, byte[][] ShortSegmentHeaderKeys) GenerateSegmentHeaderKeys(
        uint segmentIndex)
    {
        if (MaxSize >> 30 > 0x3FF)
        {
            Console.Error.WriteLine($"Invalid maximum container size '{MaxSize}' detected.");
            return (BindMode ? CasResult.ReconstructionModeError : CasResult.InvalidContainerSize, null, null);
        }

        string baseDirPath = BaseDir ?? string.Empty;

        // Generate base key
        byte[] baseKey = new byte[16];
        GetLocationHash(baseKey, baseDirPath);

        byte[][] segmentHeaderKeys = new byte[16][];
        byte[][] shortSegmentHeaderKeys = new byte[16][];

        // Compute segment header keys
        for (int i = 0; i < 16; i++)
        {
            byte[] segmentKey = new byte[16];
            Array.Copy(baseKey, segmentKey, 16);
            segmentKey[1] = (byte)segmentIndex;

            if (MaxSize >> 30 > 0x100)
            {
                segmentKey[2] = (byte)(segmentIndex >> 8);
            }

            segmentKey[0] = 0;

            for (byte j = 0; j < 0xFF; j++)
            {
                segmentKey[0] = j;
                int checksum = j;
                for (int k = 1; k < 9; k++)
                {
                    checksum ^= segmentKey[k];
                }

                if (((checksum ^ (checksum >> 4)) + 1 & 0xF) == i)
                {
                    break;
                }
            }

            segmentHeaderKeys[i] = segmentKey;
        }

        // Copy short keys
        for (int i = 0; i < 16; i++)
        {
            shortSegmentHeaderKeys[i] = new byte[9];
            Array.Copy(segmentHeaderKeys[i], shortSegmentHeaderKeys[i], 9);
        }

        return (CasResult.Success, segmentHeaderKeys, shortSegmentHeaderKeys);
    }
    
    public static void GetLocationHash(byte[] hash, string baseDirPath)
    {
        if (hash == null || hash.Length != 16)
        {
            throw new ArgumentException("Hash array must be 16 bytes.", nameof(hash));
        }

        // Use an empty string if baseDirPath is null
        baseDirPath ??= string.Empty;

        // Retrieve the computer name
        string machineName = Environment.MachineName;

        // Create an MD5 hash instance
        using (var md5 = MD5.Create())
        {
            // Hash the machine name
            md5.TransformBlock(Encoding.UTF8.GetBytes(machineName), 0, machineName.Length, null, 0);

            // Hash the base directory path
            byte[] baseDirBytes = Encoding.UTF8.GetBytes(baseDirPath);
            md5.TransformFinalBlock(baseDirBytes, 0, baseDirBytes.Length);

            // Get the resulting hash
            Buffer.BlockCopy(md5.Hash, 0, hash, 0, 16);
        }
    }
}

/*
public class CasContainerIndex
{
    public enum CasDynamicOpenMode
    {
        Reconstruction,
        Other
    }

    private uint _maxSize;
    private CasDynamicOpenMode _bindMode;
    private string _baseDir;

    public CasContainerIndex(string baseDir, uint maxSize, CasDynamicOpenMode bindMode)
    {
        _baseDir = baseDir;
        _maxSize = maxSize;
        _bindMode = bindMode;
    }

    public struct CasKey
    {
        public byte[] Data;

        public CasKey()
        {
            Data = new byte[16];
        }
    }

    public struct CasIndexKey
    {
        public byte[] Data;

        public CasIndexKey()
        {
            Data = new byte[9];
        }
    }

    public int GenerateSegmentHeaderKeys(uint segmentIndex, out CasIndexKey[] shortSegmentHeaderKeys, out CasKey[] segmentHeaderKeys)
    {
        // Ensure all keys are initialized
        shortSegmentHeaderKeys = new CasIndexKey[16];
        for (int i = 0; i < shortSegmentHeaderKeys.Length; i++)
        {
            shortSegmentHeaderKeys[i] = new CasIndexKey();
        }

        segmentHeaderKeys = new CasKey[16];
        for (int i = 0; i < segmentHeaderKeys.Length; i++)
        {
            segmentHeaderKeys[i] = new CasKey();
        }

        var baseKey = new CasKey();

        CasIndexGetLocationHash(baseKey.Data, _baseDir);

        uint maxContainerSize = _maxSize >> 30;

        if (maxContainerSize > 1023)
        {
            LogError($"Invalid maximum container size '{maxContainerSize}' detected.");
            return 8 * (_bindMode != CasDynamicOpenMode.Reconstruction ? 1 : 0) + 1;
        }

        for (int i = 0; i < 16; i++)
        {
            byte[] tempBuffer = new byte[16];
            Array.Copy(baseKey.Data, tempBuffer, baseKey.Data.Length);

            tempBuffer[8] = (byte)segmentIndex;
            if (maxContainerSize > 256)
                tempBuffer[9] = (byte)(segmentIndex >> 8);

            tempBuffer[0] = 0;

            for (byte j = 0; j < 0xFF; j++)
            {
                tempBuffer[0] = j;
                int checksum = 0;
                for (int k = 0; k < tempBuffer.Length; k++)
                {
                    checksum ^= tempBuffer[k];
                }

                if (((checksum ^ (checksum >> 4)) + 1 & 0xF) == i)
                {
                    Array.Copy(tempBuffer, 0, segmentHeaderKeys[i].Data, 0, tempBuffer.Length);
                    break;
                }
            }
        }

        // Copy the first 16 segmentHeaderKeys into shortSegmentHeaderKeys
        for (int i = 0; i < 16; i++)
        {
            Array.Copy(segmentHeaderKeys[i].Data, 0, shortSegmentHeaderKeys[i].Data, 0, shortSegmentHeaderKeys[i].Data.Length);
        }

        return 0;
    }

    private void LogError(string message)
    {
        Console.WriteLine("Error: " + message);
    }

    public void CasIndexGetLocationHash(byte[] hash, string? path)
    {
        if (hash.Length != 16)
            throw new ArgumentException("Hash must be 16 bytes.", nameof(hash));

        path ??= string.Empty;
        string machineName = Environment.MachineName;
        AnsiConsole.WriteLine($"Hostname: {machineName}");
        AnsiConsole.WriteLine($"BaseDir: {path}");

        using (var md5 = MD5.Create())
        {
            byte[] machineNameBytes = Encoding.UTF8.GetBytes(machineName);
            md5.TransformBlock(machineNameBytes, 0, machineNameBytes.Length, null, 0);

            byte[] pathBytes = Encoding.UTF8.GetBytes(path);
            md5.TransformBlock(pathBytes, 0, pathBytes.Length, null, 0);

            md5.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
            Array.Copy(md5.Hash, 0, hash, 0, 16);
        }
    }
}
*/