using System.Text;
using Spectre.Console;

namespace CASInstaller;

public class Encoding
{
    public byte version;
    public byte hash_size_ckey;
    public byte hash_size_ekey;
    public ushort CEKeyPageTable_page_size_kb;
    public ushort EKeySpecPageTable_page_size_kb;
    public uint CEKeyPageTable_page_count;
    public uint EKeySpecPageTable_page_count;
    public byte _unknown_x11;
    public ulong ESpec_block_size;
    public string[]? stringBlockEntries;
    public HeaderEntry[] contentHeaders;
    public Dictionary<Hash, FileEntry> contentEntries;
    public HeaderEntry[] encodingHeaders;
    public Dictionary<Hash, FileDescEntry> encodingEntries;
    public string encodingESpec;

    public struct HeaderEntry
    {
        public string firstHash;
        public string checksum;
    }

    public struct FileEntry
    {
        public ushort keyCount;
        public uint size;
        public Hash cKey;
        public List<Hash> eKeys;
    }

    public struct FileDescEntry
    {
        public string key;
        public uint stringIndex;
        public ulong compressedSize;
    }

    private Encoding(byte[] data, bool parseTableB, bool checkStuff)
    {
        using var stream = new MemoryStream(data);
        using var br = new BinaryReader(stream);

        var signature = br.ReadBytes(2);
        var signatureStr = System.Text.Encoding.UTF8.GetString(signature);
        if (signatureStr != "EN")
            throw new Exception("Error while parsing encoding file. Did BLTE header size change?");

        version = br.ReadByte();
        hash_size_ckey = br.ReadByte();
        hash_size_ekey = br.ReadByte();
        CEKeyPageTable_page_size_kb = br.ReadUInt16(true);
        EKeySpecPageTable_page_size_kb = br.ReadUInt16(true);
        CEKeyPageTable_page_count = br.ReadUInt32(true);
        EKeySpecPageTable_page_count = br.ReadUInt32(true);
        _unknown_x11 = br.ReadByte(); // unk
        ESpec_block_size = br.ReadUInt32(true);

        var headerLength = br.BaseStream.Position;
        var stringBlockEntries = new List<string>();

        if (parseTableB)
        {
            while ((br.BaseStream.Position - headerLength) != (long)ESpec_block_size)
            {
                stringBlockEntries.Add(br.ReadCString());
            }

            this.stringBlockEntries = stringBlockEntries.ToArray();
        }
        else
        {
            br.BaseStream.Position += (long)ESpec_block_size;
        }

        /* Table A */
        if (checkStuff)
        {
            contentHeaders = new HeaderEntry[CEKeyPageTable_page_count];

            for (int i = 0; i < CEKeyPageTable_page_count; i++)
            {
                contentHeaders[i].firstHash = Convert.ToHexString(br.ReadBytes(16));
                contentHeaders[i].checksum = Convert.ToHexString(br.ReadBytes(16));
            }
        }
        else
        {
            br.BaseStream.Position += CEKeyPageTable_page_count * 32;
        }

        var tableAstart = br.BaseStream.Position;

        contentEntries = new Dictionary<Hash, FileEntry>();

        for (int i = 0; i < CEKeyPageTable_page_count; i++)
        {
            ushort keysCount;
            while ((keysCount = br.ReadUInt16()) != 0)
            {
                var entry = new FileEntry()
                {
                    keyCount = keysCount,
                    size = br.ReadUInt32(true),
                    cKey = new Hash(br),
                    eKeys = new List<Hash>()
                };

                for (int key = 0; key < entry.keyCount; key++)
                {
                    entry.eKeys.Add(new Hash(br));
                }

                contentEntries.Add(entry.cKey, entry);
            }

            var remaining = 4096 - ((br.BaseStream.Position - tableAstart) % 4096);
            if (remaining > 0) { br.BaseStream.Position += remaining; }
        }

        if (!parseTableB)
            return;

        /* Table B */
        if (checkStuff)
        {
            encodingHeaders = new HeaderEntry[EKeySpecPageTable_page_count];

            for (int i = 0; i < EKeySpecPageTable_page_count; i++)
            {
                encodingHeaders[i].firstHash = Convert.ToHexString(br.ReadBytes(16));
                encodingHeaders[i].checksum = Convert.ToHexString(br.ReadBytes(16));
            }
        }
        else
        {
            br.BaseStream.Position += EKeySpecPageTable_page_count * 32;
        }

        var tableBstart = br.BaseStream.Position;

        encodingEntries = new Dictionary<Hash, FileDescEntry>();

        while (br.BaseStream.Position < tableBstart + 4096 * EKeySpecPageTable_page_count)
        {
            var remaining = 4096 - (br.BaseStream.Position - tableBstart) % 4096;

            if (remaining < 25)
            {
                br.BaseStream.Position += remaining;
                continue;
            }

            var key = new Hash(br);

            var entry = new FileDescEntry()
            {
                stringIndex = br.ReadUInt32(true),
                compressedSize = br.ReadUInt40(true)
            };

            if (entry.stringIndex == uint.MaxValue) break;

            encodingEntries.Add(key, entry);
        }

        // Go to the end until we hit a non-NUL byte
        while (br.BaseStream.Position < br.BaseStream.Length)
        {
            if (br.ReadByte() != 0)
                break;
        }

        br.BaseStream.Position -= 1;
        var eespecSize = br.BaseStream.Length - br.BaseStream.Position;
        encodingESpec = new string(br.ReadChars(int.Parse(eespecSize.ToString())));
    }

    public static async Task<Encoding> GetEncoding(CDN cdn, Hash key, int encodingSize = 0, bool parseTableB = false, bool checkStuff = false, bool encoded = true)
    {
        if (key.IsEmpty())
            throw new Exception("CDN or key is null.");

        BinaryReader bin;
        if (encoded)
        {
            var data = await cdn.GetData(key);
            using var ms = new MemoryStream(data);
            await using var blte = new BLTE.BLTEStream(ms, default);
            using var fso = new MemoryStream();
            await blte.CopyToAsync(fso);
            return new Encoding(fso.ToArray(), parseTableB, checkStuff);
        }
        else
        {
            var data = await File.ReadAllBytesAsync(cdn.Path);
            return new Encoding(data, parseTableB, checkStuff);
        }

        throw new Exception("Failed to download encoding file.");
    }

    public override string ToString()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"[yellow]version:[/] {version}");
        sb.AppendLine($"[yellow]hash_size_ckey:[/] {hash_size_ckey}");
        sb.AppendLine($"[yellow]hash_size_ekey:[/] {hash_size_ekey}");
        sb.AppendLine($"[yellow]CEKeyPageTable_page_size_kb:[/] {CEKeyPageTable_page_size_kb}");
        sb.AppendLine($"[yellow]EKeySpecPageTable_page_size_kb:[/] {EKeySpecPageTable_page_size_kb}");
        sb.AppendLine($"[yellow]CEKeyPageTable_page_count:[/] {CEKeyPageTable_page_count}");
        sb.AppendLine($"[yellow]EKeySpecPageTable_page_count:[/] {EKeySpecPageTable_page_count}");
        sb.AppendLine($"[yellow]_unknown_x11:[/] {_unknown_x11}");
        sb.AppendLine($"[yellow]ESpec_block_size:[/] {ESpec_block_size}");
        sb.AppendLine($"[yellow]encodingESpec:[/] {encodingESpec}");
        if (stringBlockEntries != null)
            sb.AppendLine($"[yellow]stringBlockEntries:[/] {stringBlockEntries.Length}");
        return sb.ToString();
    }

    public void Dump(string path)
    {
        using var sw = new StreamWriter(path);
        foreach (var entry in contentEntries)
        {
            sw.WriteLine($"{entry.Key},{entry.Value.eKeys[0].KeyString}");
        }
    }

    public void LogInfo()
    {
        AnsiConsole.MarkupLine("[bold blue]-------------------[/]");
        AnsiConsole.MarkupLine("[bold blue]----- Encoding ----[/]");
        AnsiConsole.MarkupLine("[bold blue]-------------------[/]");
        AnsiConsole.Markup(this.ToString());
    }
}
