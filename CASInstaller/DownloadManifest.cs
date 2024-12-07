﻿using System.Collections;
using System.Text;
using Spectre.Console;

namespace CASInstaller;

public class DownloadManifest
{
    public int m_size;
    public byte m_version;
    public byte m_eKeySize;
    public bool m_checkSumTypeConstant;
    public uint m_numEntries;
    public ushort m_numTags;
    public byte m_numFlagsSize;
    public byte m_priorityBias;
    
    public DownloadEntry[] entries;
    public TagInfo[] tags;
    
    public DownloadManifest(byte[] data)
    {
        m_size = data.Length;
        
        if (m_size <= 10)
            throw new Exception($"Detected truncated download manifest. Only got {m_size} bytes, but minimum header size is 10 bytes.");
        
        using var ms = new MemoryStream(data);
        using var br = new BinaryReader(ms);
        
        var magicBytes = br.ReadBytes(2);
        var magicString = System.Text.Encoding.UTF8.GetString(magicBytes);
        if (magicString != "DL")
            throw new Exception("Invalid magic string in download manifest.");

        m_version = br.ReadByte();
        
        if (m_version > 2)
            throw new Exception($"Unsupported install manifest version: {m_version}. This client only supports non-zero versions <= 2");
        
        m_eKeySize = br.ReadByte();
        m_checkSumTypeConstant = br.ReadByte() != 0;
        m_numEntries = br.ReadUInt32(true);
        m_numTags = br.ReadUInt16(true);
        if (m_version >= 2)
        {
            m_numFlagsSize = br.ReadByte();
            if (m_version > 4)
            {
                throw new Exception($"Unsupported number of flag bytes in download manifest: {m_numFlagsSize}");
            }

            if (m_version == 3)
            {
                m_priorityBias = br.ReadByte();
                byte[] unknown = br.ReadBytes(3);
            }
        }

        entries = new DownloadEntry[m_numEntries];
        
        for (var i = 0; i < m_numEntries; i++)
        {
            entries[i] = new DownloadEntry(br, m_eKeySize, m_checkSumTypeConstant, m_numFlagsSize);
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
        public ulong size;
        public sbyte priority;
        public uint checksum;
        public byte flags;
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
}