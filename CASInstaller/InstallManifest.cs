using System.Collections;
using System.Text;

namespace CASInstaller;

public class InstallManifest
{
    public byte version;
    public byte hashSize;
    public ushort numTags;
    public uint numEntries;
    public InstallTagEntry[] tags;
    public InstallFileEntry[] entries;
    
    public InstallManifest(byte[] data)
    {
        using var ms = new MemoryStream(data);
        using var br = new BinaryReader(ms);
        
        var marker = br.ReadBytes(2);
        var fileSignature = System.Text.Encoding.UTF8.GetString(marker);
        if (fileSignature != "IN")
            throw new Exception("Error while parsing install file. Did BLTE header size change?");

        version = br.ReadByte();        // 1
        hashSize = br.ReadByte();       // 16 (MD5)
        
        if (hashSize != 16)
            throw new Exception("Unsupported install hash size!");

        numTags = br.ReadUInt16(true);
        numEntries = br.ReadUInt32(true);

        var bytesPerTag = ((int)numEntries + 7) / 8;

        tags = new InstallTagEntry[numTags];
        
        for (var i = 0; i < numTags; i++)
        {
            tags[i] = new InstallTagEntry(br, bytesPerTag);
        }

        entries = new InstallFileEntry[numEntries];
        for (var i = 0; i < numEntries; i++)
        {
            entries[i] = new InstallFileEntry(br, numTags, hashSize, i, tags);
        }
    }
    
    public struct InstallTagEntry
    {
        public string name;
        public ushort type;
        public BitArray files;

        public InstallTagEntry(BinaryReader br, int bytesPerTag)
        {
            name = br.ReadCString();
            type = br.ReadUInt16(true);

            var fileBits = br.ReadBytes(bytesPerTag);

            for (var j = 0; j < bytesPerTag; j++)
                fileBits[j] = (byte)((fileBits[j] * 0x0202020202 & 0x010884422010) % 1023);

            files = new BitArray(fileBits);
        }
    }

    public struct InstallFileEntry
    {
        public string name;
        public Hash contentHash;
        public uint size;
        public HashSet<string> tagStrings;

        public InstallFileEntry(BinaryReader br, int numTags, int hashSize, int i, InstallTagEntry[] tags)
        {
            name = br.ReadCString();
            contentHash = new Hash(br.ReadBytes(hashSize));
            size = br.ReadUInt32(true);
            tagStrings = [];
            for (var j = 0; j < numTags; j++)
            {
                if (tags[j].files[i] == true)
                {
                    tagStrings.Add(tags[j].name);
                }
            }
        }
    }
    
    public static async Task<InstallManifest?> GetInstall(CDN cdn, string? key)
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
            
            return new InstallManifest(data);
        }

        return null;
    }

    public override string ToString()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"[yellow]Version:[/] {version}");
        sb.AppendLine($"[yellow]Hash Size:[/] {hashSize}");
        sb.AppendLine($"[yellow]Num Tags:[/] {numTags}");
        sb.AppendLine($"[yellow]Num Entries:[/] {numEntries}");
        sb.Append("[yellow]Tags:[/]");
        foreach (var tag in tags)
        {
            sb.Append($"{tag.name} ");
        }
        sb.AppendLine();
        /*
        sb.AppendLine("[yellow]Entries:[/]");
        foreach (var entry in entries)
        {
            sb.AppendLine($"{entry.contentHash} [yellow]{entry.name}[/] {entry.size}");
        }
        */
        return sb.ToString();
    }
}