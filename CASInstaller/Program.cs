using System.Collections.Concurrent;
using System.Security.Cryptography;
using Spectre.Console;

namespace CASInstaller;

internal class Program
{
    private const string _product = "wow_classic_era";
    private const string _branch = "eu";
    private const string _installPath = "";
    
    static async Task Main(string[] args)
    {
        BLTE.BLTEStream.ValidateData = false;
        
        var cdn = await ProcessCDN(_product, _branch);
        var version = await ProcessVersion(_product, _branch);
        var (shared_game_dir, data_dir, tags) = await ProcessProductConfig(cdn, version);
        var buildConfig = await ProcessBuildConfig(cdn, version, data_dir);
        await ProcessCDNConfig(cdn, version, data_dir);
        var encoding = await ProcessEncoding(buildConfig, cdn);
        await ProcessDownload(buildConfig, cdn, tags);
        await ProcessInstall(buildConfig, cdn, tags, encoding, shared_game_dir);
    }

    private static async Task<CDN> ProcessCDN(string product, string branch)
    {
        var cdn = await CDN.GetCDN(product, branch);
        
        AnsiConsole.MarkupLine("[bold blue]---------------[/]");
        AnsiConsole.MarkupLine("[bold blue]----- CDN -----[/]");
        AnsiConsole.MarkupLine("[bold blue]---------------[/]");
        AnsiConsole.Markup(cdn.ToString());
        
        return cdn;
    }

    private static async Task<Version> ProcessVersion(string product, string branch)
    {
        var version = await Version.GetVersion(product, branch);
    
        AnsiConsole.MarkupLine("[bold blue]---------------[/]");
        AnsiConsole.MarkupLine("[bold blue]--- Version ---[/]");
        AnsiConsole.MarkupLine("[bold blue]---------------[/]");
        AnsiConsole.Markup(version.ToString());
        return version;
    }
    
    private static async Task<(string shared_game_dir, string data_dir, List<string> tags)> ProcessProductConfig(CDN cdn, Version version)
    {
        var productConfig = await ProductConfig.GetProductConfig(cdn, version.ProductConfigHash.KeyString);
        if (productConfig == null)
        {
            AnsiConsole.MarkupLine("[red]Product Config not found![/]");
            return (string.Empty, string.Empty, []);
        }
        
        var tags = productConfig.platform.win.config.tags;
        var tags_64bit = productConfig.platform.win.config.tags_64bit;
        var installTags = new List<string>();
        installTags.AddRange(tags);
        installTags.AddRange(tags_64bit);
        
        var game_dir = Path.Combine(_installPath, productConfig.all.config.form.game_dir.dirname);
        var shared_game_dir = Path.Combine(game_dir, productConfig.all.config.shared_container_default_subfolder);
        if (!Directory.Exists(shared_game_dir))
            Directory.CreateDirectory(shared_game_dir);
        
        var data_dir = Path.Combine(game_dir, productConfig.all.config.data_dir);
        if (!Directory.Exists(data_dir))
            Directory.CreateDirectory(data_dir);
        
        return (shared_game_dir, data_dir, installTags);
    }
    
    private static async Task<BuildConfig> ProcessBuildConfig(CDN cdn, Version version, string data_dir)
    {
        var buildConfig = await BuildConfig.GetBuildConfig(cdn, version.BuildConfigHash.KeyString, data_dir);
        
        AnsiConsole.MarkupLine("[bold blue]-------------------[/]");
        AnsiConsole.MarkupLine("[bold blue]--- Build Config --[/]");
        AnsiConsole.MarkupLine("[bold blue]-------------------[/]");
        AnsiConsole.Markup(buildConfig.ToString());
        return buildConfig;
    }
    
    const int BlockSize = 4096;
    
    private static async Task ProcessCDNConfig(CDN cdn, Version version, string data_dir)
    {
        var cdnConfig = await CDNConfig.GetConfig(cdn, version.CdnConfigHash.KeyString, data_dir);
        
        AnsiConsole.MarkupLine("[bold blue]-------------------[/]");
        AnsiConsole.MarkupLine("[bold blue]--- CDN Config ----[/]");
        AnsiConsole.MarkupLine("[bold blue]-------------------[/]");
        AnsiConsole.Markup(cdnConfig.ToString());
        
        var fileIndex = FileIndex.GetDataIndex(cdn, cdnConfig.FileIndex, "data", data_dir).Result;
        using var sw = new StreamWriter("fileIndex.txt");
        foreach (var entry in fileIndex.Entries)
        {
            sw.WriteLine($"{entry.Key},{entry.Value.size}");
        }
        
        var patchFileIndex = FileIndex.GetDataIndex(cdn, cdnConfig.PatchFileIndex, "patch", data_dir).Result;

        var archiveGroup = new ConcurrentDictionary<Hash, ArchiveIndex.IndexEntry>();
        var patchGroup = new ConcurrentDictionary<Hash, ArchiveIndex.IndexEntry>();

        AnsiConsole.Progress().Start(ctx =>
        {
            if (cdnConfig.Archives != null)
            {
                var task1 = ctx.AddTask($"[green]Processing {cdnConfig.Archives.Length} archives[/]");
                task1.MaxValue = cdnConfig.Archives.Length;
                Parallel.For(0, cdnConfig.Archives.Length, i =>
                {
                    var archive = cdnConfig.Archives[i];
                    task1.Increment(1);
                    var index = ArchiveIndex.GetDataIndex(cdn, archive, "data", data_dir, archiveGroup, (ushort)i).Result;
                });
            }

            if (cdnConfig.PatchArchives != null)
            {
                var task2 = ctx.AddTask($"[green]Processing {cdnConfig.PatchArchives.Length} patch archives[/]");
                task2.MaxValue = cdnConfig.PatchArchives.Length;
                Parallel.For(0, cdnConfig.PatchArchives.Length, i =>
                {
                    var archive = cdnConfig.PatchArchives[i];
                    task2.Increment(1);
                    var index = ArchiveIndex.GetDataIndex(cdn, archive, "patch", data_dir, patchGroup, (ushort)i).Result;
                });
            }
        });
        
        var archiveKeys = new List<Hash>(archiveGroup.Keys);
        archiveKeys.Sort();
        
        var saveDir = Path.Combine(data_dir, "indices");
        var archiveGroupPath = Path.Combine(saveDir, cdnConfig.ArchiveGroup.KeyString + ".index");

        await using var fs = new FileStream(archiveGroupPath, FileMode.Create, FileAccess.Write);
        await using var bw = new BinaryWriter(fs);

        // Buffer to hold the current block
        var blockBuffer = new MemoryStream();
        await using var blockWriter = new BinaryWriter(blockBuffer);

        List<Hash> lastBlockEkeys = new();
        List<byte[]> md5s = new();
        
        Hash previousHash = default;
        foreach (var hash in archiveKeys)
        {
            // If the block buffer exceeds the block size, flush it to the file
            if (blockBuffer.Length + 26 >= BlockSize)
            {
                lastBlockEkeys.Add(previousHash);
                var md5 = await WriteBlockToFileAsync(fs, blockBuffer, true);
                md5s.Add(md5);
            }
            
            var entry = archiveGroup[hash];

            // Write entry to block buffer
            blockWriter.Write(hash.Key);
            blockWriter.Write(entry.size, true);
            blockWriter.Write(entry.archiveIndex, true);
            blockWriter.Write(entry.offset, true);
            
            previousHash = hash;
        }

        lastBlockEkeys.Add(previousHash);
        
        // Write the final block and pad if necessary
        if (blockBuffer.Length > 0)
        {
            var md5 = await WriteBlockToFileAsync(fs, blockBuffer, true);
            md5s.Add(md5);
        }

        // Write the TOC
        // Write last block ekeys
        using var tocMS = new MemoryStream();
        await using var tocBW = new BinaryWriter(tocMS);

        // Write keys to the TOC
        foreach (var key in lastBlockEkeys)
        {
            tocBW.Write(key.Key);
        }

        // Write the lower part of MD5 hashes
        foreach (var md5 in md5s)
        {
            tocBW.Write(md5, 0, 8);
        }

        // Calculate the MD5 hash of the TOC
        tocBW.Flush(); // Ensure all data is written to the MemoryStream
        tocMS.Position = 0; // Reset the stream position for hashing

        using var md5Hasher = MD5.Create();
        var tocMD5 = await md5Hasher.ComputeHashAsync(tocMS);
        
        // Optionally, save the TOC to a file if needed
        tocMS.Position = 0; // Reset position after hashing
        await tocMS.CopyToAsync(fs);
        
        // Write the TOC MD5 hash to the file
        bw.Write(tocMD5, 0, 8);
        
        using var footerMS = new MemoryStream();
        await using var footerBW = new BinaryWriter(footerMS);
        
        // Write version
        footerBW.Write((byte)1);
        
        // Write _11
        footerBW.Write((byte)0);
        
        // Write _12
        footerBW.Write((byte)0);
        
        // Write block size
        footerBW.Write((byte)4);
        
        // Write offset bytes
        footerBW.Write((byte)6);
        
        // Write size bytes
        footerBW.Write((byte)4);
        
        // Write key size bytes
        footerBW.Write((byte)16);
        
        // Write checksum size
        footerBW.Write((byte)8);
        
        // Write number of elements
        footerBW.Write(archiveKeys.Count);
        
        footerBW.Write(new byte[8]);
        
        footerBW.Flush(); // Ensure all data is written to the MemoryStream
        footerMS.Position = 0; // Reset the stream position for hashing
        
        var footerMD5 = await md5Hasher.ComputeHashAsync(footerMS);
        
        footerMS.Position = 0; // Reset position after hashing
        await footerMS.CopyToAsync(fs);
        
        // Write the footer MD5 hash to the file
        bw.BaseStream.Position -= 8;
        bw.Write(footerMD5, 0, 8);
    }
    
    // Method to write a block to the file, pad if necessary, and return the MD5 hash
    static async Task<byte[]> WriteBlockToFileAsync(Stream stream, MemoryStream blockBuffer, bool pad = false)
    {
        if (pad)
        {
            var padding = BlockSize - blockBuffer.Length;
            if (padding > 0)
            {
                blockBuffer.Write(new byte[padding], 0, (int)padding);
            }
        }

        // Reset position to calculate MD5 and copy to stream
        blockBuffer.Position = 0;

        // Calculate MD5 hash
        using var md5 = MD5.Create();
        var hash = await md5.ComputeHashAsync(blockBuffer);

        // Write the block to the stream
        blockBuffer.Position = 0; // Reset position after hashing
        await blockBuffer.CopyToAsync(stream);

        // Reset the block buffer for reuse
        blockBuffer.SetLength(0);

        // Return the MD5 hash
        return hash;
    }
    
    private static async Task<Encoding> ProcessEncoding(BuildConfig buildConfig, CDN cdn)
    {
        var encodingContentHash = buildConfig.Encoding[0];
        var encodingEncodedHash = buildConfig.Encoding[1];
        var encoding = await Encoding.GetEncoding(cdn, encodingEncodedHash);

        await using var sw = new StreamWriter("encoding.txt");
        foreach (var entry in encoding.contentEntries)
        {
            await sw.WriteLineAsync($"{entry.Key},{entry.Value.eKeys[0].KeyString}");
        }
        
        AnsiConsole.MarkupLine("[bold blue]-------------------[/]");
        AnsiConsole.MarkupLine("[bold blue]----- Encoding ----[/]");
        AnsiConsole.MarkupLine("[bold blue]-------------------[/]");
        AnsiConsole.Markup(encoding.ToString());
        return encoding;
    }
    
    private static async Task ProcessInstall(BuildConfig buildConfig, CDN cdn, List<string> tags, Encoding encoding, string shared_game_dir)
    {
        var installContentHash = buildConfig.Install[0];
        var installEncodedHash = buildConfig.Install[1];
        var install = await InstallManifest.GetInstall(cdn, installEncodedHash);
        
        AnsiConsole.MarkupLine("[bold blue]-------------------[/]");
        AnsiConsole.MarkupLine("[bold blue]----- Install -----[/]");
        AnsiConsole.MarkupLine("[bold blue]-------------------[/]");
        
        if (install == null)
        {
            AnsiConsole.MarkupLine("[red]Install not found![/]");
            return;
        }
        AnsiConsole.Markup(install.ToString());
        
        // Filter entries by tags
        var tagFilteredEntries = new List<InstallManifest.InstallFileEntry>();
        var tagIndexMap = new Dictionary<string, int>();
        for (var i = 0; i < install.tags.Length; i++)
        {
            tagIndexMap[install.tags[i].name] = i;
        }
        foreach (var entry in install.entries)
        {
            var checksOut = true;
            foreach (var tag in tags)
            {
                if (!entry.tagIndices.Contains(tagIndexMap[tag]))
                {
                    checksOut = false;
                }
            }
            
            if (checksOut)
                tagFilteredEntries.Add(entry);
        }

        await using var sw = new StreamWriter("install.txt");
        foreach (var entry in tagFilteredEntries)
        {
            await sw.WriteLineAsync($"{entry.contentHash},{entry.name}");
        }
        
        /*
        Parallel.ForEach(tagFilteredEntries, installEntry =>
        {
            if (!encoding.contentEntries.TryGetValue(installEntry.contentHash, out var entry)) return;
            var key = entry.eKeys[0].KeyString;
            if (key == null) return;
            foreach (var cdnURL in cdn.Hosts)
            {
                var url = $@"http://{cdnURL}/{cdn.Path}/data/{key[0..2]}/{key[2..4]}/{key}";
                var encryptedData = Utils.GetDataFromURL(url).Result;

                if (encryptedData == null)
                {
                    AnsiConsole.MarkupLine($"[red]{installEntry.name}[/]");
                    continue;
                }

                byte[]? data;
                if (ArmadilloCrypt.Instance == null)
                    data = encryptedData;
                else
                    data = ArmadilloCrypt.Instance?.DecryptData(key, encryptedData);

                if (data == null) continue;

                using var ms = new MemoryStream(data);
                using var blte = new BLTE.BLTEStream(ms, default);
                using var fso = new MemoryStream();
                blte.CopyTo(fso);
                data = fso.ToArray();

                var dir = Path.GetDirectoryName(installEntry.name) ?? "";
                var dirPath = Path.Combine(shared_game_dir, dir);
                if (!Directory.Exists(dirPath))
                    Directory.CreateDirectory(dirPath);

                var filePath = Path.Combine(shared_game_dir, installEntry.name);

                if (File.Exists(filePath))
                    continue;

                File.WriteAllBytesAsync(filePath, data);
            }
        });
        */
    }

    private static async Task ProcessDownload(BuildConfig buildConfig, CDN cdn, List<string> tags)
    {
        var downloadContentHash = buildConfig.Download[0];
        var downloadEncodedHash = buildConfig.Download[1];
        var download = await DownloadManifest.GetDownload(cdn, downloadEncodedHash);
        
        AnsiConsole.MarkupLine("[bold blue]-------------------[/]");
        AnsiConsole.MarkupLine("[bold blue]----- Download -----[/]");
        AnsiConsole.MarkupLine("[bold blue]-------------------[/]");
        AnsiConsole.Markup(download.ToString());

        // Add locale tags
        //tags.Clear();
        tags.Add("enUS");

        //var testIDX = new IDX("Z:\\World of Warcraft\\Data\\data\\0000000010.idx");
        
        if (File.Exists("download_index0.txt"))
            File.Delete("download_index0.txt");
        using var sw = new StreamWriter("download_index0.txt");

        ulong totalDownloadSize = 0;

        var tagFilteredEntries = new List<DownloadManifest.DownloadEntry>();
        var tagIndexMap = new Dictionary<string, int>();
        for (var i = 0; i < download.tags.Length; i++)
        {
            tagIndexMap[download.tags[i].name] = i;
        }
        
        foreach (var entry in download.entries)
        {
            var checksOut = true;
            foreach (var tag in tags)
            {
                if ((entry.tagIndices & (1 << tagIndexMap[tag])) == 0)
                {
                    checksOut = false;
                    break;
                }
            }

            if (checksOut)
                tagFilteredEntries.Add(entry);
        }
        
        foreach (var entry in tagFilteredEntries)
        {
            if (entry.priority is 0 or 1)
            {
                totalDownloadSize += entry.size;
                var index = cascGetBucketIndex(entry.eKey);
                if (index == 0)
                {
                    sw.WriteLine(entry.eKey.KeyString);
                    /*
                    var key9 = new Hash(entry.eKey.Key, true);
                    if (!testIDX.LocalIndexData.ContainsKey(key9))
                    {
                        AnsiConsole.WriteLine();
                        AnsiConsole.MarkupLine($"[red]EKey:[/] {entry.eKey}");
                        AnsiConsole.MarkupLine($"[red]Size:[/] {entry.size}");
                        AnsiConsole.MarkupLine($"[red]Priority:[/] {entry.priority}");
                        AnsiConsole.Markup($"[red]Tags:[/]");
                        foreach (var tag in tagIndexMap)
                        {
                            if ((entry.tagIndices & (1 << tag.Value)) != 0)
                                AnsiConsole.Markup($"{tag.Key} ");
                        }
                        AnsiConsole.WriteLine();
                    }
                    */
                }
                //sw.WriteLine($"{entry.eKey},{entry.size},{entry.priority},{entry.checksum},{entry.flags}");
            }
        }
        AnsiConsole.MarkupLine($"[bold green]Total download size: {totalDownloadSize}[/]");
    }

    private static int cascGetBucketIndex(Hash key)
    {
        var k = key.Key;
        var i = k[0] ^ k[1] ^ k[2] ^ k[3] ^ k[4] ^ k[5] ^ k[6] ^ k[7] ^ k[8];
        return (i & 0xf) ^ (i >> 4);
    }
    
    private static int cascGetBucketIndexCrossReference(Hash key)
    {
        var i = cascGetBucketIndex(key);
        return (i + 1) % 16;
    }
}