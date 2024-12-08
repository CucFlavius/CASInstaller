using System.Security.Cryptography;
using System.Text;

public enum CasDynamicOpenMode
{
    Reconstruction,
    Other
}

public class CasContainerIndex
{
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

    public int GenerateSegmentHeaderKeys(uint segmentIndex, CasIndexKey[] shortSegmentHeaderKeys, CasKey[] segmentHeaderKeys)
    {
        if (shortSegmentHeaderKeys == null || segmentHeaderKeys == null)
            throw new ArgumentNullException("Key arrays cannot be null.");

        // Ensure all keys are initialized
        for (int i = 0; i < shortSegmentHeaderKeys.Length; i++)
        {
            shortSegmentHeaderKeys[i] = new CasIndexKey();
        }

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

    private void CasIndexGetLocationHash(byte[] hash, string? path)
    {
        if (hash.Length != 16)
            throw new ArgumentException("Hash must be 16 bytes.", nameof(hash));

        path ??= string.Empty;
        string machineName = Environment.MachineName;

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
