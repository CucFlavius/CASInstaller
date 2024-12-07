using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using Google.Protobuf;
using Google.Protobuf.Collections;
using ProtoDatabase;
using Spectre.Console;

namespace CASInstaller;

public class Game(string product, string branch = "us")
{
    private const string cache_dir = "cache";
    private readonly string? _product = product;
    private readonly string? _branch = branch;
    
    private string? _installPath;
    private CDN? _cdn;
    private Version? _version;
    private ProductConfig? _productConfig;
    private string? _game_dir;
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
        if (!Directory.Exists(cache_dir))
            Directory.CreateDirectory(cache_dir);
        
        _installPath = installPath;

        BLTE.BLTEStream.ThrowOnMissingDecryptionKey = false;
        KeyService.LoadKeys();
        
        _cdn = await CDN.GetCDN(_product, _branch);
        _cdn?.LogInfo();
        
        _version = await Version.GetVersion(_product, _branch);
        _version?.LogInfo();

        _productConfig = await ProductConfig.GetProductConfig(_cdn, _version?.ProductConfigHash);
        ProcessProductConfig(_productConfig, _installPath);
        
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
        _install?.Dump("install.txt");
        _install?.LogInfo();
        await ProcessInstall(_install, _cdn, _cdnConfig, _encoding, _archiveGroup, _installTags, _shared_game_dir);

        WriteProductDB(_game_dir, _version, _productConfig, _buildConfig);
        WritePatchResult(_game_dir);
        WriteLauncherDB(_game_dir);
    }

    private void ProcessProductConfig(ProductConfig? productConfig, string installPath)
    {
        if (productConfig == null)
            return;
        
        var tags = productConfig.platform.win.config.tags;
        var tags_64bit = productConfig.platform.win.config.tags_64bit;
        _installTags = [];
        _installTags.AddRange(tags);
        _installTags.AddRange(tags_64bit);
        
        _game_dir = Path.Combine(installPath, productConfig.all.config.form.game_dir.dirname);
        _shared_game_dir = Path.Combine(_game_dir, productConfig.all.config.shared_container_default_subfolder);
        if (!Directory.Exists(_shared_game_dir))
            Directory.CreateDirectory(_shared_game_dir);
        
        _data_dir = Path.Combine(_game_dir, productConfig.all.config.data_dir);
        if (!Directory.Exists(_data_dir))
            Directory.CreateDirectory(_data_dir);
    }

    private static async Task<ConcurrentDictionary<Hash, ArchiveIndex.IndexEntry>?> ProcessIndices(
        CDN? cdn, CDNConfig? cdnConfig, Hash[]? indices, Hash? groupKey, string? data_dir,
        string pathType, byte offsetBytes, bool overrideGroup = false)
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
    
    private static async Task ProcessInstall(
        InstallManifest? install, CDN? cdn, CDNConfig? cdnConfig, Encoding? encoding,
        ConcurrentDictionary<Hash, ArchiveIndex.IndexEntry>? archiveGroup, List<string>? tags,
        string? shared_game_dir, bool overrideFiles = false)
    {
        if (install == null || cdn == null || encoding == null || archiveGroup == null || shared_game_dir == null)
            return;
        
        tags ??= [];
        tags.Add("enUS");
        
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
        
        //Parallel.ForEach(tagFilteredEntries, async void (installEntry) =>
        foreach (var installEntry in tagFilteredEntries)
        {
            if (!encoding.contentEntries.TryGetValue(installEntry.contentHash, out var contentEntry)) return;
            var key = contentEntry.eKeys[0];

            var filePath = Path.Combine(shared_game_dir, installEntry.name);
            
            if (File.Exists(filePath) && !overrideFiles)
                continue;

            byte[]? data = null;
            
            if (archiveGroup.TryGetValue(key, out var indexEntry))
            {
                try
                {
                    data = await DownloadFileFromIndex(indexEntry, cdn, cdnConfig);

                    // Install file is BLTE encoded
                    if (data != null)
                    {
                        using var ms = new MemoryStream(data);
                        await using var blte = new BLTE.BLTEStream(ms, default);
                        using var fso = new MemoryStream();
                        await blte.CopyToAsync(fso);
                        data = fso.ToArray();
                    }
                }
                catch (CASInstaller.BLTE.BLTEDecoderException be)
                {
                    AnsiConsole.WriteLine($"[red]BLTE Decoder Exception: {be.Message}[/]");
                }
                catch (Exception e)
                {
                    AnsiConsole.WriteException(e);
                    throw;
                }
            }
            else
            {
                data = await DownloadFileDirectly(key, cdn);
            }
            
            if (data == null) continue;
            
            var dirPath = Path.GetDirectoryName(filePath) ?? "";
            if (!Directory.Exists(dirPath))
                Directory.CreateDirectory(dirPath);

            await File.WriteAllBytesAsync(filePath, data);
        }
    }

    private static async Task<byte[]?> DownloadFileFromIndex(ArchiveIndex.IndexEntry indexEntry, CDN? cdn, CDNConfig? cdnConfig)
    {
        var hosts = cdn?.Hosts;
        if (hosts == null) return null;
        var archive = cdnConfig?.Archives?[indexEntry.archiveIndex];
        if (archive != null)
        {
            foreach (var cdnURL in hosts)
            {
                var url = $@"http://{cdnURL}/{cdn?.Path}/data/{archive.Value.UrlString}";
                var dataFilePath = Path.Combine(cache_dir, archive.Value.KeyString);
                if (File.Exists(dataFilePath))
                {
                    await using var str = File.OpenRead(dataFilePath);
                    var data = new byte[indexEntry.size];
                    str.Seek(indexEntry.offset, SeekOrigin.Begin);
                    await str.ReadExactlyAsync(data, 0, (int)indexEntry.size);
                    return data;
                }
                else
                {
                    var encryptedData = await Utils.GetDataFromURL(url);
                    if (encryptedData == null)
                        continue;

                    byte[]? decryptedData;
                    if (ArmadilloCrypt.Instance == null)
                        decryptedData = encryptedData;
                    else
                        decryptedData = ArmadilloCrypt.Instance?.DecryptData(archive.Value, encryptedData);

                    if (decryptedData == null) continue;

                    // Cache
                    await File.WriteAllBytesAsync(dataFilePath, decryptedData);
                    
                    using var str = new MemoryStream(decryptedData);
                    var data = new byte[indexEntry.size];
                    str.Seek(indexEntry.offset, SeekOrigin.Begin);
                    await str.ReadExactlyAsync(data, 0, (int)indexEntry.size);
                    return data;
                }
            }
        }
        
        return null;
    }
    
    private static async Task<byte[]?> DownloadFileDirectly(Hash key, CDN? cdn)
    {
        var hosts = cdn?.Hosts;
        if (hosts == null) return null;
        
        foreach (var cdnURL in hosts)
        {
            var url = $@"http://{cdnURL}/{cdn?.Path}/data/{key.UrlString}";
            var encryptedData = await Utils.GetDataFromURL(url);

            if (encryptedData == null)
                continue;

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

            return data;
        }
        
        return null;
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
    
    private void WriteProductDB(string? gameDir, Version? version, ProductConfig? productConfig, BuildConfig? buildConfig)
    {
        var prod = new ProductInstall
        {
            Uid = _product,
            ProductCode = _product,
            Settings = new UserSettings()
            {
                InstallPath = gameDir,
                PlayRegion = "us",
                DesktopShortcut = 0,
                StartmenuShortcut = 0,
                LanguageSettings = ProtoDatabase.LanguageSettingType.LangsettingAdvanced,
                SelectedTextLanguage = "enUS",
                SelectedSpeechLanguage = "enUS",
                AdditionalTags = "",
                VersionBranch = "",
                AccountCountry = "",    // TODO: Find US country code
                GeoIpCountry = "",      // TODO: Find US country code
                GameSubfolder = productConfig?.all.config.shared_container_default_subfolder ?? "",
            },
            CachedProductState = new CachedProductState()
            {
                BaseProductState = new BaseProductState()
                {
                    Installed = true,
                    Playable = true,
                    UpdateComplete = true,
                    BackgroundDownloadAvailable = false,
                    BackgroundDownloadComplete = false,
                    CurrentVersion = "",
                    CurrentVersionStr = version?.VersionsName ?? "",
                    DecryptionKey = "",     // TODO: Needs armadillo key name?
                    ActiveBuildKey = version?.BuildConfigHash.KeyString?.ToLower() ?? "",
                    ActiveBgdlKey = "",
                    ActiveInstallKey = "",
                    // TODO: v
                    ActiveTagString = @"Windows x86_64 US? acct-ROU? geoip-RO? enUS speech?:Windows x86_64 US? acct-ROU? geoip-RO? enUS text?",
                    IncompleteBuildKey = ""
                },
                BackfillProgress = new BackfillProgress()
                {
                    Progress = 0,
                    Backgrounddownload = false,
                    Paused = false,
                    DownloadLimit = 0,
                    Remaining = 0,
                    Details = null
                },
                RepairProgress = new RepairProgress()
                {
                    Progress = 0,
                },
                UpdateProgress = new UpdateProgress()
                {
                    LastDiscSetUsed = "",
                    Progress = 1,
                    DiscIgnored = false,
                    TotalToDownload = 4428913417,   // TODO: ???
                    DownloadRemaining = 0,
                    Details = new BuildProgressDetails()
                    {
                        TargetKey = version?.BuildConfigHash.KeyString?.ToLower() ?? "",
                        WrittenOffset =
                        {
                            31965942,       // TODO: ???
                            0,
                            0
                        },
                        DownloadBaseline =
                        {
                            376442172,      // TODO: ???
                            2881546372,     // TODO: ???
                            1170768299      // TODO: ???
                        }
                    }
                },
            },
            ProductOperations = null,
            ProductFamily = buildConfig?.BuildProduct.ToLower() ?? "wow",
            Hidden = false,
            PersistentJsonStorage = @"{\u0022user_install_package_settings\u0022:[]}"
        };
        
        if (gameDir == null)
            return;

        var data = prod.ToByteArray();
        var path = Path.Combine(gameDir, ".product.db");
        if (File.Exists(path))
            File.SetAttributes(path, FileAttributes.None);
            
        File.WriteAllBytes(path, data);
        File.SetAttributes(path, FileAttributes.Hidden);
    }
    
    private void WritePatchResult(string? gameDir)
    {
        if (gameDir == null)
            return;

        var path = Path.Combine(gameDir, ".patch.result");
        if (File.Exists(path))
            File.SetAttributes(path, FileAttributes.None);

        var data = "0"u8.ToArray();
        File.WriteAllBytes(path, data);
        File.SetAttributes(path, FileAttributes.Hidden);
    }
    
    private void WriteLauncherDB(string? gameDir)
    {
        if (gameDir == null)
            return;
        
        var path = Path.Combine(gameDir, "Launcher.db");
        if (File.Exists(path))
            File.SetAttributes(path, FileAttributes.None);
        
        var data = "enUS"u8.ToArray();
        File.WriteAllBytes(path, data);
        File.SetAttributes(path, FileAttributes.Hidden);
    }
}