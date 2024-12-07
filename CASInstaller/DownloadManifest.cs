using System.Collections;
using System.Text;
using Spectre.Console;

namespace CASInstaller;

public class DownloadManifest
{
    public byte version;
    public byte checksumSize;
    public bool hasChecksumInEntry;
    public uint entryCount;
    public ushort tagCount;
    public byte flagSize;
    public List<DownloadEntry> entries;
    public List<DownloadTag> tags;
    
    public DownloadManifest(byte[] data)
    {
        using var ms = new MemoryStream(data);
        using var br = new BinaryReader(ms);
        
        var marker = br.ReadBytes(2);
        var fileSignature = System.Text.Encoding.UTF8.GetString(marker);
        if (fileSignature != "DL")
            throw new Exception("Error while parsing install file. Did BLTE header size change?");

        version = br.ReadByte();
        checksumSize = br.ReadByte();
        hasChecksumInEntry = br.ReadByte() != 0;
        entryCount = br.ReadUInt32(true);
        tagCount = br.ReadUInt16(true);
        if (version >= 2)
        {
            flagSize = br.ReadByte();
            if (version >= 3)
            {
                byte base_priority = br.ReadByte();
                byte[] unknown = br.ReadBytes(3);
            }
        }

        entries = new List<DownloadEntry>();
        
        for (var i = 0; i < entryCount; i++)
        {
            var entry = new DownloadEntry(br, checksumSize, hasChecksumInEntry, flagSize);
            entries.Add(entry);
        }
        
        tags = new List<DownloadTag>();
        var bytesPerTag = ((int)entryCount + 7) / 8;
        
        for (var i = 0; i < tagCount; i++)
        {
            tags.Add(new DownloadTag(br, bytesPerTag));
        }
    }
    
    public struct DownloadEntry
    {
        public Hash eKey;
        public ulong size;
        public byte priority;
        public uint checksum;
        public byte flags;

        public DownloadEntry(BinaryReader br, byte checksumSize, bool hasChecksumInEntry, byte flagSize)
        {
            eKey = new Hash(br.ReadBytes(checksumSize));
            size = br.ReadUInt40(true);
            priority = br.ReadByte();
            
            if (hasChecksumInEntry)
            {
                checksum = br.ReadUInt32(true);
            }

            if (flagSize == 1)
            {
                flags = br.ReadByte();
            }
            else if (flagSize != 0)
            {
                throw new Exception("Unexpected download flag size");
            }
        }
    }
    
    public struct DownloadTag
    {
        public string name;
        public ushort type;
        public BitArray files;

        public DownloadTag(BinaryReader br, int bytesPerTag)
        {
            name = br.ReadCString();
            type = br.ReadUInt16(true);

            var fileBits = br.ReadBytes(bytesPerTag);

            for (var j = 0; j < bytesPerTag; j++)
                fileBits[j] = (byte)((fileBits[j] * 0x0202020202 & 0x010884422010) % 1023);

            files = new BitArray(fileBits);
        }
    }
    
    public static async Task<DownloadManifest?> GetDownload(CDN cdn, string? key)
    {
        foreach (var cdnURL in cdn.Hosts)
        {
            var url = $@"http://{cdnURL}/{cdn.Path}/data/{key[0..2]}/{key[2..4]}/{key}";
            var encryptedData = await Utils.GetDataFromURL(url);
            if (encryptedData == null) continue;

            byte[]? data;
            if (ArmadilloCrypt.Instance == null)
                data = encryptedData;
            else
                data = ArmadilloCrypt.Instance?.DecryptData(key, encryptedData);
            
            if (data == null) continue;
            
            using var ms = new MemoryStream(data);
            await using var blte = new BLTE.BLTEStream(ms, default);
            using var fso = new MemoryStream();
            await blte.CopyToAsync(fso);
            data = fso.ToArray();
            
            return new DownloadManifest(data);
        }

        return null;
    }

    public override string ToString()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"[yellow]Version:[/] {version}");
        sb.AppendLine($"[yellow]Hash Size:[/] {checksumSize}");
        sb.AppendLine($"[yellow]Has Checksum In Entry:[/] {hasChecksumInEntry}");
        sb.AppendLine($"[yellow]Num Entries:[/] {entryCount}");
        sb.AppendLine($"[yellow]Num Tags:[/] {tagCount}");
        sb.AppendLine($"[yellow]Flag Size:[/] {flagSize}");

        return sb.ToString();
    }
}