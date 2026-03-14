using System.Text;
using Spectre.Console;

namespace CASInstaller;

public class DownloadManifest
{
    private readonly byte m_version;
    private readonly byte m_eKeySize;
    private readonly bool m_checkSumTypeConstant;
    private readonly uint m_numEntries;
    private readonly ushort m_numTags;
    private readonly byte m_numFlagsSize;
    public byte m_priorityBias;

    public readonly DownloadEntry[] entries;
    public readonly TagInfo[] tags;

    private DownloadManifest(byte[] data)
    {
        var mSize = data.Length;

        if (mSize <= 10)
            throw new Exception($"Detected truncated download manifest. Only got {mSize} bytes, but minimum header size is 10 bytes.");

        using var ms = new MemoryStream(data);
        using var br = new BinaryReader(ms);

        var magicBytes = br.ReadBytes(2);
        var magicString = System.Text.Encoding.UTF8.GetString(magicBytes);
        if (magicString != "DL")
            throw new Exception("Invalid magic string in download manifest.");

        m_version = br.ReadByte();

        if (m_version is 0 or > 3)
            throw new Exception($"Unsupported download manifest version: {m_version}. This client only supports versions 1-3");

        m_eKeySize = br.ReadByte();
        m_checkSumTypeConstant = br.ReadByte() != 0;
        m_numEntries = br.ReadUInt32(true);
        m_numTags = br.ReadUInt16(true);
        if (m_version >= 2)
        {
            m_numFlagsSize = br.ReadByte();
            switch (m_version)
            {
                case > 4:
                    throw new Exception($"Unsupported number of flag bytes in download manifest: {m_numFlagsSize}");
                case 3:
                {
                    m_priorityBias = br.ReadByte();
                    br.ReadBytes(3);
                    break;
                }
            }
        }

        entries = new DownloadEntry[m_numEntries];

        for (var i = 0; i < m_numEntries; i++)
        {
            entries[i] = new DownloadEntry(br, m_eKeySize, m_checkSumTypeConstant, m_numFlagsSize);
            if (m_version >= 3)
                entries[i].priority = (sbyte)(entries[i].priority - m_priorityBias);
        }

        tags = new TagInfo[m_numTags];
        var bytesPerTag = ((int)m_numEntries + 7) / 8;

        for (var i = 0; i < m_numTags; i++)
        {
            tags[i] = new TagInfo(br, bytesPerTag);
        }

        for (var i = 0; i < m_numEntries; i++)
        {
            entries[i].tagIndices = 0; // Initialize to 0

            for (var j = 0; j < m_numTags; j++)
            {
                if (tags[j].bitmap[i])
                {
                    entries[i].tagIndices |= (1 << j); // Set the bit at position j
                }
            }
        }
    }

    public class DownloadEntry
    {
        public Hash eKey;
        public readonly ulong size;
        public sbyte priority;
        private readonly uint checksum;
        private readonly byte flags;
        public int tagIndices;

        public DownloadEntry(BinaryReader br, byte checksumSize, bool hasChecksumInEntry, byte flagSize)
        {
            eKey = new Hash(br.ReadBytes(checksumSize));
            size = br.ReadUInt40(true);
            priority = br.ReadSByte();

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

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"[yellow]Key:[/] {eKey}");
            sb.AppendLine($"[yellow]Size:[/] {size}");
            sb.AppendLine($"[yellow]Priority:[/] {priority}");
            sb.AppendLine($"[yellow]Checksum:[/] {checksum}");
            sb.AppendLine($"[yellow]Flags:[/] {flags}");
            sb.AppendLine($"[yellow]Tags:[/] {tagIndices}");
            return sb.ToString();
        }
    }

    public static async Task<DownloadManifest?> GetDownload(CDN cdn, Hash key)
    {
        var encryptedData = await cdn.GetData(key);
        if (encryptedData == null) return null;

        var data = ArmadilloCrypt.Instance == null ? encryptedData : ArmadilloCrypt.Instance.DecryptData(key, encryptedData);

        using var ms = new MemoryStream(data);
        await using var blte = new BLTE.BLTEStream(ms, default);
        var decompressed = new byte[blte.Length];
        int readOffset = 0;
        while (readOffset < decompressed.Length)
        {
            var read = blte.Read(decompressed, readOffset, decompressed.Length - readOffset);
            if (read == 0) break;
            readOffset += read;
        }

        return new DownloadManifest(decompressed);
    }

    public override string ToString()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"[yellow]Version:[/] {m_version}");
        sb.AppendLine($"[yellow]Hash Size:[/] {m_eKeySize}");
        sb.AppendLine($"[yellow]Has Checksum In Entry:[/] {m_checkSumTypeConstant}");
        sb.AppendLine($"[yellow]Num Entries:[/] {m_numEntries}");
        sb.AppendLine($"[yellow]Num Tags:[/] {m_numTags}");
        sb.AppendLine($"[yellow]Flag Size:[/] {m_numFlagsSize}");
        sb.Append("[yellow]Tags:[/]");
        foreach (var tag in tags)
        {
            sb.Append($"{tag.name} ");
        }
        sb.AppendLine();
        return sb.ToString();
    }

    public void LogInfo()
    {
        AnsiConsole.MarkupLine("[bold blue]-------------------[/]");
        AnsiConsole.MarkupLine("[bold blue]----- Download -----[/]");
        AnsiConsole.MarkupLine("[bold blue]-------------------[/]");
        AnsiConsole.Markup(this.ToString());
    }

    public void Dump(string path)
    {
        using var sw = new StreamWriter(path);
        sw.WriteLine($"Version: {m_version}");
        sw.WriteLine($"EKeySize: {m_eKeySize}");
        sw.WriteLine($"HasChecksum: {m_checkSumTypeConstant}");
        sw.WriteLine($"NumEntries: {m_numEntries}");
        sw.WriteLine($"NumTags: {m_numTags}");
        sw.WriteLine($"FlagSize: {m_numFlagsSize}");
        sw.WriteLine($"PriorityBias: {m_priorityBias}");
        sw.WriteLine();

        sw.WriteLine("=== TAGS ===");
        for (var i = 0; i < tags.Length; i++)
        {
            var tag = tags[i];
            var setBits = 0;
            for (var j = 0; j < tag.bitmap.Length; j++)
            {
                if (tag.bitmap[j]) setBits++;
            }
            sw.WriteLine($"  Tag[{i}]: name=\"{tag.name}\" type={tag.type} entries={setBits}/{m_numEntries}");
        }
        sw.WriteLine();

        sw.WriteLine("=== PRIORITY DISTRIBUTION ===");
        var priorityCounts = new Dictionary<int, int>();
        foreach (var entry in entries)
        {
            if (!priorityCounts.ContainsKey(entry.priority))
                priorityCounts[entry.priority] = 0;
            priorityCounts[entry.priority]++;
        }
        foreach (var (priority, count) in priorityCounts.OrderBy(kv => kv.Key))
        {
            sw.WriteLine($"  Priority {priority}: {count} entries");
        }
        sw.WriteLine();

        sw.WriteLine("=== TAG FILTERING ANALYSIS ===");
        // Simulate filtering with different tag sets
        var tagIndexMap = new Dictionary<string, int>();
        for (var i = 0; i < tags.Length; i++)
            tagIndexMap[tags[i].name] = i;

        var tagSets = new Dictionary<string, List<string>>
        {
            ["Windows+x86_64+enUS"] = ["Windows", "x86_64", "enUS"],
            ["Windows+x86_64+enUS+text"] = ["Windows", "x86_64", "enUS", "text"],
            ["Windows+x86_64+enUS+speech"] = ["Windows", "x86_64", "enUS", "speech"],
            ["Windows+x86_64+enUS+US"] = ["Windows", "x86_64", "enUS", "US"],
            ["Windows+x86_64"] = ["Windows", "x86_64"],
            ["Windows+enUS"] = ["Windows", "enUS"],
            ["x86_64+enUS"] = ["x86_64", "enUS"],
            ["Windows"] = ["Windows"],
            ["x86_64"] = ["x86_64"],
            ["enUS"] = ["enUS"],
        };

        foreach (var (label, filterTags) in tagSets)
        {
            var matching = 0;
            var matchingP012 = 0;
            foreach (var entry in entries)
            {
                var checksOut = true;
                foreach (var tag in filterTags)
                {
                    if (tagIndexMap.TryGetValue(tag, out var tagIndex))
                    {
                        if ((entry.tagIndices & (1 << tagIndex)) == 0)
                        {
                            checksOut = false;
                            break;
                        }
                    }
                }
                if (checksOut)
                {
                    matching++;
                    if (entry.priority is 0 or 1 or 2)
                        matchingP012++;
                }
            }
            sw.WriteLine($"  {label}: {matching} total, {matchingP012} with priority 0-2");
        }
        sw.WriteLine();

        // Cross-tabulation: for entries matching Windows+x86_64+enUS, which other tags do they have?
        sw.WriteLine("=== CROSS-TAB: Entries matching Windows+x86_64+enUS ===");
        if (tagIndexMap.TryGetValue("Windows", out var winIdx) &&
            tagIndexMap.TryGetValue("x86_64", out var x64Idx) &&
            tagIndexMap.TryGetValue("enUS", out var enIdx))
        {
            var winBit = 1 << winIdx;
            var x64Bit = 1 << x64Idx;
            var enBit = 1 << enIdx;
            var matchMask = winBit | x64Bit | enBit;

            var otherTagCounts = new Dictionary<string, int>();
            var totalMatching = 0;
            foreach (var entry in entries)
            {
                if ((entry.tagIndices & matchMask) != matchMask) continue;
                totalMatching++;
                for (var i = 0; i < tags.Length; i++)
                {
                    if (i == winIdx || i == x64Idx || i == enIdx) continue;
                    if ((entry.tagIndices & (1 << i)) != 0)
                    {
                        var tagName = tags[i].name;
                        if (!otherTagCounts.ContainsKey(tagName))
                            otherTagCounts[tagName] = 0;
                        otherTagCounts[tagName]++;
                    }
                }
            }
            sw.WriteLine($"  Total: {totalMatching}");
            foreach (var (tag, count) in otherTagCounts.OrderByDescending(kv => kv.Value))
            {
                sw.WriteLine($"    Also has \"{tag}\": {count}");
            }
        }
        sw.WriteLine();

        // Agent-style tag query simulation: (Win+x64+enUS+text) UNION (Win+x64+enUS+speech)
        sw.WriteLine("=== AGENT-STYLE TAG QUERY SIMULATION ===");
        if (tagIndexMap.TryGetValue("Windows", out var winIdx2) &&
            tagIndexMap.TryGetValue("x86_64", out var x64Idx2) &&
            tagIndexMap.TryGetValue("enUS", out var enIdx2))
        {
            var winBit2 = 1 << winIdx2;
            var x64Bit2 = 1 << x64Idx2;
            var enBit2 = 1 << enIdx2;
            var baseMask = winBit2 | x64Bit2 | enBit2;

            var textBit = tagIndexMap.TryGetValue("text", out var textIdx) ? (1 << textIdx) : 0;
            var speechBit = tagIndexMap.TryGetValue("speech", out var speechIdx) ? (1 << speechIdx) : 0;
            var usBit = tagIndexMap.TryGetValue("US", out var usIdx) ? (1 << usIdx) : 0;

            var countBase = 0;           // Win+x64+enUS (our current filter)
            var countTextOnly = 0;       // Win+x64+enUS+text (section 1)
            var countSpeechOnly = 0;     // Win+x64+enUS+speech (section 2)
            var countUnion = 0;          // text UNION speech (Agent result)
            var countNeither = 0;        // Win+x64+enUS but neither text nor speech
            var countWithUS = 0;         // also US
            var countUnionUS = 0;        // (text UNION speech) AND US

            foreach (var entry in entries)
            {
                if ((entry.tagIndices & baseMask) != baseMask) continue;
                countBase++;
                var hasText = textBit != 0 && (entry.tagIndices & textBit) != 0;
                var hasSpeech = speechBit != 0 && (entry.tagIndices & speechBit) != 0;
                var hasUS = usBit != 0 && (entry.tagIndices & usBit) != 0;

                if (hasText) countTextOnly++;
                if (hasSpeech) countSpeechOnly++;
                if (hasText || hasSpeech) countUnion++;
                if (!hasText && !hasSpeech) countNeither++;
                if (hasUS) countWithUS++;
                if ((hasText || hasSpeech) && hasUS) countUnionUS++;
            }

            sw.WriteLine($"  Our filter (Win+x64+enUS):                    {countBase}");
            sw.WriteLine($"  Agent Section1 (Win+x64+enUS+text):           {countTextOnly}");
            sw.WriteLine($"  Agent Section2 (Win+x64+enUS+speech):         {countSpeechOnly}");
            sw.WriteLine($"  Agent Union (text OR speech):                  {countUnion}");
            sw.WriteLine($"  Entries with NEITHER text nor speech:          {countNeither}");
            sw.WriteLine($"  EXCESS (our - agent union):                    {countBase - countUnion}");
            sw.WriteLine($"  With US region:                                {countWithUS}");
            sw.WriteLine($"  Agent Union + US:                              {countUnionUS}");
            sw.WriteLine($"  EXCESS with US (our - agent+US):               {countBase - countUnionUS}");
        }
        sw.WriteLine();

        // Show unique tagIndices combinations for matching entries
        sw.WriteLine("=== TAG COMBINATION ANALYSIS ===");
        var comboCounts = new Dictionary<int, int>();
        foreach (var entry in entries)
        {
            if (!comboCounts.ContainsKey(entry.tagIndices))
                comboCounts[entry.tagIndices] = 0;
            comboCounts[entry.tagIndices]++;
        }
        sw.WriteLine($"  Unique tag combinations: {comboCounts.Count}");
        foreach (var (combo, count) in comboCounts.OrderByDescending(kv => kv.Value))
        {
            var tagNames = new List<string>();
            for (var i = 0; i < tags.Length; i++)
            {
                if ((combo & (1 << i)) != 0)
                    tagNames.Add(tags[i].name);
            }
            sw.WriteLine($"  [{string.Join(", ", tagNames)}] = {count} entries");
        }
    }
}
