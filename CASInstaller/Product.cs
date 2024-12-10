using System.Collections.Concurrent;
using Google.Protobuf;
using ProtoDatabase;
using Spectre.Console;

namespace CASInstaller;

public partial class Product
{
    private const string cache_dir = "cache";

    private readonly string? _product;
    private readonly string? _branch;
    private readonly InstallSettings _installSettings;
    private string? _installPath;
    private CDN? _cdn;
    private Version? _version;
    public ProductConfig? _productConfig;
    public string? _game_dir;
    public string? _shared_game_dir;
    public string? _data_dir;
    public List<string>? _installTags;
    private BuildConfig? _buildConfig;
    private CDNConfig? _cdnConfig;
    private FileIndex? _fileIndex;
    private FileIndex? _patchFileIndex;
    private ConcurrentDictionary<Hash, ArchiveIndex.IndexEntry>? _archiveGroup;
    private ConcurrentDictionary<Hash, ArchiveIndex.IndexEntry>? _patchGroup;
    private Encoding? _encoding;
    private DownloadManifest? _download;
    private InstallManifest? _install;
    private Dictionary<byte, IDX>? idxMap;
    private string? _gameDataDir;
    private BuildInfo? _buildInfo;
    private List<byte[][]>? segmentHeaderKeyList;
    private Data? Working_Data { get; set; }
    
    public Product(string? product, string? branch = "us", InstallSettings installSettings = default!)
    {
        _product = product;
        _branch = branch;
        _installSettings = installSettings;
    }
    
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
        
        _encoding = await Encoding.GetEncoding(_cdn, _buildConfig?.Encoding[1], 0, true);
        _encoding?.LogInfo();
        _encoding?.Dump("encoding.txt");
        
        _download = await DownloadManifest.GetDownload(_cdn, _buildConfig?.Download[1]);
        _download?.LogInfo();

        _install = await InstallManifest.GetInstall(_cdn, _buildConfig?.Install[1]);
        _install?.Dump("install.txt");
        _install?.LogInfo();
        
        // Download data //
        _gameDataDir = Path.Combine(_data_dir ?? "", "data");
        if (!Directory.Exists(_gameDataDir))
            Directory.CreateDirectory(_gameDataDir);
        
        BuildIDXMap(_gameDataDir);
        
        await DownloadAndWriteFile((Hash)_buildConfig?.Download[1]!, _cdn, _cdnConfig, _archiveGroup, (ulong)int.Parse(_buildConfig?.DownloadSize[1]!));
        await DownloadAndWriteFile((Hash)_buildConfig?.Install[1]!, _cdn, _cdnConfig, _archiveGroup, (ulong)int.Parse(_buildConfig?.InstallSize[1]!));
        await DownloadAndWriteFile((Hash)_buildConfig?.Encoding[1]!, _cdn, _cdnConfig, _archiveGroup, (ulong)int.Parse(_buildConfig?.EncodingSize[1]!));
        await DownloadAndWriteFile((Hash)_buildConfig?.Size[1]!, _cdn, _cdnConfig, _archiveGroup, (ulong)int.Parse(_buildConfig?.SizeSize[1]!));
        
        await ProcessDownload(_download, _cdn, _cdnConfig, _encoding, _archiveGroup, _patchGroup, _installTags, _data_dir);
        await ProcessInstall(_install, _cdn, _cdnConfig, _encoding, _archiveGroup, _installTags, _shared_game_dir);

        Working_Data?.Finalize(_gameDataDir, idxMap!);

        WriteIDXMap();
        
        if (_installSettings.CreateProductDB)
            WriteProductDB(_game_dir, _version, _productConfig, _buildConfig);
        if (_installSettings.CreatePatchResult)
            WritePatchResult(_game_dir);
        if (_installSettings.CreateLauncherDB)
            WriteLauncherDB(_game_dir);
        if (_installSettings.CreateBuildInfo)
            WriteBuildInfo(_game_dir);
        if (_installSettings.CreateFlavorInfo)
            WriteFlavorInfo(_shared_game_dir, _version);
    }

    private void ProcessProductConfig(ProductConfig? productConfig, string installPath)
    {
        if (productConfig == null)
            return;
        
        var tags = productConfig.platform.win.config?.tags;
        var tags_64bit = productConfig.platform.win.config?.tags_64bit;
        _installTags ??= [];
        if (tags != null)
            _installTags.AddRange(tags);
        if (tags_64bit != null)
            _installTags.AddRange(tags_64bit);
        
        if (productConfig.all?.config?.form?.game_dir?.dirname != null)
            _game_dir = Path.Combine(installPath, productConfig.all.config.form.game_dir.dirname);
        if (!Directory.Exists(_game_dir))
            Directory.CreateDirectory(_game_dir!);
        if (productConfig.all?.config?.shared_container_default_subfolder != null)
            _shared_game_dir = Path.Combine(_game_dir!, productConfig.all.config.shared_container_default_subfolder);
        if (!Directory.Exists(_shared_game_dir))
            Directory.CreateDirectory(_shared_game_dir!);
        
        if (productConfig?.all?.config?.data_dir != null)
            _data_dir = Path.Combine(_game_dir!, productConfig.all.config.data_dir);
        if (!Directory.Exists(_data_dir))
            Directory.CreateDirectory(_data_dir!);
    }

    private static async Task<ConcurrentDictionary<Hash, ArchiveIndex.IndexEntry>?> ProcessIndices(
        CDN? cdn, CDNConfig? cdnConfig, Hash[]? indices, Hash? groupKey, string? data_dir,
        string pathType, byte offsetBytes, bool overrideGroup = false)
    {
        if (cdn == null || cdnConfig == null || groupKey == null)
            return null;
        
        var indexGroup = new ConcurrentDictionary<Hash, ArchiveIndex.IndexEntry>();

        var saveDir = Path.Combine(data_dir ?? "", "indices");
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
            if (data_dir != null)
                ArchiveIndex.GenerateIndexGroupFile(indexGroup, groupPath, offsetBytes);
        }

        return indexGroup;
    }
    
    private static async Task ProcessInstall(
        InstallManifest? install, CDN? cdn, CDNConfig? cdnConfig, Encoding? encoding,
        ConcurrentDictionary<Hash, ArchiveIndex.IndexEntry>? archiveGroup, List<string>? tags,
        string? shared_game_dir, bool overrideFiles = false)
    {
        if (install == null || cdn == null || encoding == null || shared_game_dir == null)
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
                if (tagIndexMap.TryGetValue(tag, out var tagIndex))
                {
                    if (!entry.tagIndices.Contains(tagIndex))
                    {
                        checksOut = false;
                    }
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
            
            if (archiveGroup != null && archiveGroup.TryGetValue(key, out var indexEntry))
            {
                try
                {
                    data = await Data.DownloadFileFromIndex(indexEntry, cdn, cdnConfig);
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
                data = await Data.DownloadFileDirectly(key, cdn);
            }
            
            if (data == null) continue;
            
            // Decode BLTE, install files must be extracted
            using var ms = new MemoryStream(data);
            await using var blte = new BLTE.BLTEStream(ms, default);
            using var fso = new MemoryStream();
            await blte.CopyToAsync(fso);
            data = fso.ToArray();
            
            var dirPath = Path.GetDirectoryName(filePath) ?? "";
            if (!Directory.Exists(dirPath))
                Directory.CreateDirectory(dirPath);

            await File.WriteAllBytesAsync(filePath, data);
        }
    }
    
    private async Task ProcessDownload(
        DownloadManifest? download, CDN? cdn, CDNConfig? cdnConfig, Encoding? encoding,
        ConcurrentDictionary<Hash, ArchiveIndex.IndexEntry>? archiveGroup,
        ConcurrentDictionary<Hash, ArchiveIndex.IndexEntry>? patchGroup, List<string>? tags,
        string? data_dir, bool overrideFiles = false)
    {
        if (download == null)
            return;

        tags ??= [];
        
        // Add locale tags
        tags.Add("enUS");

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
                tagFilteredEntries.Add(entry);
        }

        await AnsiConsole.Progress().StartAsync(async ctx =>
        {
            var length = tagFilteredEntries?.Count ?? 0;
            var task1 = ctx.AddTask($"[green]Processing {length} download entries[/]");
            task1.MaxValue = length;
            
            foreach (var downloadEntry in tagFilteredEntries)
            {
                task1.Increment(1);
                if (downloadEntry.priority is 0 or 1)
                {
                    await DownloadAndWriteFile(downloadEntry.eKey, cdn, cdnConfig, archiveGroup, downloadEntry.size);
                }
            }
        });
        AnsiConsole.MarkupLine($"[bold green]Total download size: {totalDownloadSize}[/]");
    }

    private void BuildIDXMap(string dataDir)
    {
        const byte idxN = 1;        // Only need to increase this number if we update the game
        idxMap = new Dictionary<byte, IDX>();
        for (var i = 0; i < 16; i++)
        {
            var bucket = (byte)i;
            var path = Path.Combine(dataDir, $"{bucket:X2}000000{idxN:X2}.idx");
            idxMap.Add(bucket, new IDX(path, bucket));
        }
    }

    private void WriteIDXMap()
    {
        if (idxMap == null)
            throw new NullReferenceException("idxMap");
        
        // Add idx entries for every segment header Meta in every data file
        for (var d = 0; d < segmentHeaderKeyList.Count; d++)
        {
            for (var i = 0; i < 16; i++)
            {
                var key = segmentHeaderKeyList[d][i];
                //var bucket = Data.cascGetBucketIndex(key);
                //if (idxMap.TryGetValue(bucket, out var sidx))
                foreach (var (_,sidx) in idxMap)
                {
                    _ = sidx.Add(new Hash(key), d, i * 30, 30);
                }
            }
        }

        foreach (var (key, idx) in idxMap)
        {
            if (File.Exists(idx.Path))
                File.Delete(idx.Path);

            idx.Write();
        }
    }

    private async Task DownloadAndWriteFile(Hash eKey, CDN? cdn, CDNConfig? cdnConfig, ConcurrentDictionary<Hash, ArchiveIndex.IndexEntry>? archiveGroup, ulong size)
    {
        var writeSize = size + 30;
        var bucket = Data.cascGetBucketIndex(eKey);

        if (_gameDataDir == null)
            throw new NullReferenceException("_gameDataDir");
        
        if (idxMap == null)
            throw new NullReferenceException("idxMap");

        if (Working_Data == null)
        {
            Working_Data = new Data(0, out var segmentHeaderKeys);
            segmentHeaderKeyList = [ segmentHeaderKeys ];
        }

        if (idxMap.TryGetValue(bucket, out var idx))
        {
            if (Working_Data.CanWrite((int)writeSize))
            {
                var idxEntry = idx.Add(eKey, Working_Data.ID, (int)Working_Data.Offset, writeSize);
                await Working_Data.WriteDataEntry(idxEntry, cdn, cdnConfig, archiveGroup);
            }
            else
            {
                Working_Data.Finalize(_gameDataDir, idxMap!);
                var currentID = Working_Data.ID;
                Working_Data = new Data(currentID + 1, out var segmentHeaderKeys);
                segmentHeaderKeyList.Add(segmentHeaderKeys);
            }
        }
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
    
    private void WriteBuildInfo(string? gameDir)
    {
        if (gameDir == null)
            return;
        
        var path = Path.Combine(gameDir, ".build.info");
            
        _buildInfo = new BuildInfo(path);
        var build = new BuildInfo.Build
        {
            Branch = _cdn?.Name,
            Active = 1,
            BuildKey = _version?.BuildConfigHash ?? new Hash(),
            CDNKey = _version?.CdnConfigHash ?? new Hash(),
            InstallKey = _buildConfig?.Install[1] ?? new Hash(),
            IMSize = -1,
            CDNPath = _cdn?.Path,
            CDNHosts = _cdn?.Hosts,
            CDNServers = _cdn?.Servers,
            Tags = ["Windows", "x86_64", "EU?", "acct-ROU?", "geoip-RO?", "enUS", "speech?:Windows", "x86_64", "EU?", "acct-ROU?", "geoip-RO?", "enUS", "text?" ],
            Armadillo = "",
            LastActivated = "",
            Version = _version?.VersionsName,
            KeyRing = _version?.KeyRing ?? new Hash(),
            Product = _version?.Product
        };

        _buildInfo.AddBuild(build);
        _buildInfo.Write(path);
    }
    
    private void WriteFlavorInfo(string? shared_game_dir, Version? version)
    {
        if (shared_game_dir == null)
            return;

        var filePath = Path.Combine(shared_game_dir, ".flavor.info");
        using var sw = new StreamWriter(filePath);
        sw.WriteLine("Product Flavor!STRING:0");
        sw.WriteLine(version?.Product);
    }
}