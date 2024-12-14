using System.Text;
using Spectre.Console;

namespace CASInstaller;

public class InstallManifest
{
    public int m_size;
    public byte m_version;
    public byte m_cKeySize;
    public ushort m_numTags;
    public uint m_numEntries;
    public TagInfo[] tags;
    public InstallFileEntry[] entries;
    
    public InstallManifest(byte[] data)
    {
        m_size = data.Length;

        if (m_size <= 9)
            throw new Exception($"Detected truncated install manifest. Only got {m_size} bytes, but minimum header size is 9 bytes.");
        
        using var ms = new MemoryStream(data);
        using var br = new BinaryReader(ms);
        
        var magicBytes = br.ReadBytes(2);
        var magicString = System.Text.Encoding.UTF8.GetString(magicBytes);
        if (magicString != "IN")
            throw new Exception("Invalid magic string in install manifest.");

        m_version = br.ReadByte();
        
        if (m_version != 1)
            throw new Exception($"Unsupported install manifest version: {m_version}. This client only supports non-zero versions <= 1");
        
        m_cKeySize = br.ReadByte();       // 16 (MD5)
        
        if (m_cKeySize != 16)
            throw new Exception("Unsupported install hash size!");

        m_numTags = br.ReadUInt16(true);
        m_numEntries = br.ReadUInt32(true);

        var m_bitmapSize = (int)((m_numEntries + 7) >> 3);

        tags = new TagInfo[m_numTags];
        
        for (var i = 0; i < m_numTags; i++)
        {
            tags[i] = new TagInfo(br, m_bitmapSize);
        }

        entries = new InstallFileEntry[m_numEntries];
        for (var i = 0; i < m_numEntries; i++)
        {
            entries[i] = new InstallFileEntry(br, m_numTags, m_cKeySize, i, tags);
        }
    }

    public struct InstallFileEntry
    {
        public string name;
        public Hash contentHash;
        public uint size;
        public HashSet<int> tagIndices;

        public InstallFileEntry(BinaryReader br, int numTags, int hashSize, int i, TagInfo[] tags)
        {
            name = br.ReadCString();
            contentHash = new Hash(br.ReadBytes(hashSize));
            size = br.ReadUInt32(true);
            tagIndices = [];
            for (var j = 0; j < numTags; j++)
            {
                if (tags[j].bitmap[i] == true)
                {
                    tagIndices.Add(j);
                }
            }
        }
    }
    
    public static async Task<InstallManifest?> GetInstall(CDN? cdn, Hash? key)
    {
        var hosts = cdn?.Hosts;
        if (hosts == null) return null;
        foreach (var cdnURL in hosts)
        {
            var url = $@"http://{cdnURL}/{cdn?.Path}/data/{key?.UrlString}";
            var encryptedData = await cdn.GetDataFromURL(url);
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
        sb.AppendLine($"[yellow]Version:[/] {m_version}");
        sb.AppendLine($"[yellow]Hash Size:[/] {m_cKeySize}");
        sb.AppendLine($"[yellow]Num Tags:[/] {m_numTags}");
        sb.AppendLine($"[yellow]Num Entries:[/] {m_numEntries}");
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

    public void LogInfo()
    {
        AnsiConsole.MarkupLine("[bold blue]-------------------[/]");
        AnsiConsole.MarkupLine("[bold blue]----- Install -----[/]");
        AnsiConsole.MarkupLine("[bold blue]-------------------[/]");
        AnsiConsole.Markup(this.ToString());
    }

    public void Dump(string path)
    {
        using var sw = new StreamWriter(path);
        foreach (var entry in entries)
        {
            sw.WriteLine($"{entry.contentHash},{entry.name}");
        }
    }
}