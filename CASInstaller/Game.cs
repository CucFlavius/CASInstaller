using System.Collections.Concurrent;
using System.Security.Cryptography;
using Spectre.Console;

namespace CASInstaller;

public class Game(string product, string branch = "us")
{
    private readonly string? _product = product;
    private readonly string? _branch = branch;
    
    private string? _installPath;
    private CDN? _cdn;
    private Version? _version;
    private ProductConfig? _productConfig;
    private string? _shared_game_dir;
    private string? _data_dir;
    private List<string>? _installTags;
    private BuildConfig? _buildConfig;
    private CDNConfig? _cdnConfig;
    private FileIndex? _fileIndex;
    private FileIndex? _patchFileIndex;
    private ConcurrentDictionary<Hash, ArchiveIndex.IndexEntry>? _archiveGroup;
    private ConcurrentDictionary<Hash, ArchiveIndex.IndexEntry>? _patchGroup;
    private Encoding? _encoding;
    private DownloadManifest? _download;
    private InstallManifest? _install;

    public async Task Install(string installPath)
    {
        _installPath = installPath;
        
        _cdn = await CDN.GetCDN(_product, _branch);
        _cdn?.LogInfo();
        
        _version = await Version.GetVersion(_product, _branch);
        _version?.LogInfo();

        _productConfig = await ProductConfig.GetProductConfig(_cdn, _version?.ProductConfigHash);
        ProcessProductConfig(_productConfig);
        
        _buildConfig = await BuildConfig.GetBuildConfig(_cdn, _version?.BuildConfigHash, _data_dir);
        _buildConfig?.LogInfo();
        
        _cdnConfig = await CDNConfig.GetConfig(_cdn, _version?.CdnConfigHash, _data_dir);
        _cdnConfig?.LogInfo();
        
        _fileIndex = await FileIndex.GetDataIndex(_cdn, _cdnConfig?.FileIndex, "data", _data_dir);
        _fileIndex?.Dump("fileindex.txt");
        
        _patchFileIndex = FileIndex.GetDataIndex(_cdn, _cdnConfig?.PatchFileIndex, "patch", _data_dir).Result;
        _patchFileIndex?.Dump("patchfileindex.txt");
        
        _archiveGroup = await ProcessIndices(_cdn, _cdnConfig, _cdnConfig?.Archives, _cdnConfig?.ArchiveGroup, _data_dir, "data", 6);
        _patchGroup = await ProcessIndices(_cdn, _cdnConfig, _cdnConfig?.PatchArchives, _cdnConfig?.PatchArchiveGroup, _data_dir, "patch", 5);
        
        var encodingContentHash = _buildConfig?.Encoding[0];
        var encodingEncodedHash = _buildConfig?.Encoding[1];
        _encoding = await Encoding.GetEncoding(_cdn, encodingEncodedHash);
        _encoding?.LogInfo();
        _encoding?.Dump("encoding.txt");
        
        var downloadContentHash = _buildConfig?.Download[0];
        var downloadEncodedHash = _buildConfig?.Download[1];
        _download = await DownloadManifest.GetDownload(_cdn, downloadEncodedHash);
        _download?.LogInfo();
        ProcessDownload(_download, _installTags);

        var installContentHash = _buildConfig?.Install[0];
        var installEncodedHash = _buildConfig?.Install[1];
        _install = await InstallManifest.GetInstall(_cdn, installEncodedHash);
        _install?.LogInfo();
        
        await ProcessInstall(_install, _installTags);
    }
    
    private void ProcessProductConfig(ProductConfig? productConfig)
    {
        if (productConfig == null)
            return;
        
        var tags = productConfig.platform.win.config.tags;
        var tags_64bit = productConfig.platform.win.config.tags_64bit;
        _installTags = [];
        _installTags.AddRange(tags);
        _installTags.AddRange(tags_64bit);
        
        var game_dir = Path.Combine(_installPath, productConfig.all.config.form.game_dir.dirname);
        _shared_game_dir = Path.Combine(game_dir, productConfig.all.config.shared_container_default_subfolder);
        if (!Directory.Exists(_shared_game_dir))
            Directory.CreateDirectory(_shared_game_dir);
        
        _data_dir = Path.Combine(game_dir, productConfig.all.config.data_dir);
        if (!Directory.Exists(_data_dir))
            Directory.CreateDirectory(_data_dir);
    }

    private static async Task<ConcurrentDictionary<Hash, ArchiveIndex.IndexEntry>?> ProcessIndices(
        CDN? cdn, CDNConfig? cdnConfig, Hash[]? indices,  Hash? groupKey, string? data_dir, string pathType, byte offsetBytes, bool overrideGroup = false)
    {
        if (cdn == null || cdnConfig == null || data_dir == null || groupKey == null)
            return null;
        
        var indexGroup = new ConcurrentDictionary<Hash, ArchiveIndex.IndexEntry>();

        var saveDir = Path.Combine(data_dir, "indices");
        var groupPath = Path.Combine(saveDir, groupKey?.KeyString + ".index");

        if (File.Exists(groupPath) && !overrideGroup)
        {
            // Load existing archive group index
            _ = await ArchiveIndex.GetDataIndex(cdn, groupKey, pathType, data_dir, indexGroup, (ushort)0);
            AnsiConsole.WriteLine(indexGroup.Count);
        }
        else
        {
            AnsiConsole.Progress().Start(ctx =>
            {
                if (indices == null) return;
                var length = indices?.Length ?? 0;
                var task1 = ctx.AddTask($"[green]Processing {indices?.Length} indices[/]");
                task1.MaxValue = length;
                Parallel.For(0, length, i =>
                {
                    var indexKey = indices?[i];
                    task1.Increment(1);
                    _ = ArchiveIndex.GetDataIndex(cdn, indexKey, pathType, data_dir, indexGroup, (ushort)i).Result;
                });
                
            });
            
            // Save the archive group index
            ArchiveIndex.GenerateIndexGroupFile(indexGroup, groupPath, offsetBytes);
        }

        return indexGroup;
    }
    
    private static async Task ProcessInstall(InstallManifest? install, List<string>? tags)
    {
        if (install == null)
            return;
        
        tags ??= [];
        
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
            var key = entry.eKeys[0];
            if (key == null) return;
            foreach (var cdnURL in cdn.Hosts)
            {
                var url = $@"http://{cdnURL}/{cdn.Path}/data/{key?.UrlString}";
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

    private void ProcessDownload(DownloadManifest? download, List<string>? tags)
    {
        if (download == null)
            return;

        tags ??= [];
        
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