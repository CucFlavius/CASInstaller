using System.Collections.Concurrent;
using Google.Protobuf;
using ProtoDatabase;
using Spectre.Console;

namespace CASInstaller;

public class Game(string product, string branch = "us")
{
    private readonly byte[] reconstruction_hash = [0xD9, 0x4B, 0xCD, 0x8C, 0x43, 0x18, 0x6B, 0x30, 0xEC, 0xC7, 0xDB, 0xE1, 0x11, 0x00];
    private const int DATA_TOTAL_SIZE_MAXIMUM = 1023 * 1024 * 1024;
    private const int DATA_OFFSET_START = 480;
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
    private Dictionary<byte, IDX> idxMap;
    private string gameDataDir;

    public int Working_Data { get; set; } = 0;
    public int Working_Offset { get; set; } = DATA_OFFSET_START;
    public MemoryStream? Working_Stream { get; set; }

    public async Task Install(string installPath)
    {
        //TestGenerateSegmentHeaderKeys();
        //return;
        
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
        _encoding = await Encoding.GetEncoding(_cdn, encodingEncodedHash, 0, true);
        _encoding?.LogInfo();
        _encoding?.Dump("encoding.txt");
        
        var downloadContentHash = _buildConfig?.Download[0];
        var downloadEncodedHash = _buildConfig?.Download[1];
        _download = await DownloadManifest.GetDownload(_cdn, downloadEncodedHash);
        _download?.LogInfo();

        var installContentHash = _buildConfig?.Install[0];
        var installEncodedHash = _buildConfig?.Install[1];
        _install = await InstallManifest.GetInstall(_cdn, installEncodedHash);
        _install?.Dump("install.txt");
        _install?.LogInfo();
        
        // Download data //
        gameDataDir = Path.Combine(_data_dir ?? "", "data");
        if (!Directory.Exists(gameDataDir))
            Directory.CreateDirectory(gameDataDir);
        
        BuildIDXMap(gameDataDir);
        
        await DownloadAndWriteFile((Hash)downloadEncodedHash!, _cdn, _cdnConfig, _archiveGroup, (ulong)int.Parse(_buildConfig?.DownloadSize[1]!));
        await DownloadAndWriteFile((Hash)installEncodedHash!, _cdn, _cdnConfig, _archiveGroup, (ulong)int.Parse(_buildConfig?.InstallSize[1]!));
        await DownloadAndWriteFile((Hash)encodingEncodedHash!, _cdn, _cdnConfig, _archiveGroup, (ulong)int.Parse(_buildConfig?.EncodingSize[1]!));
        await DownloadAndWriteFile((Hash)_buildConfig?.Size[1]!, _cdn, _cdnConfig, _archiveGroup, (ulong)int.Parse(_buildConfig?.SizeSize[1]!));
        
        await ProcessDownload(_download, _cdn, _cdnConfig, _encoding, _archiveGroup, _patchGroup, _installTags, _data_dir);
        await ProcessInstall(_install, _cdn, _cdnConfig, _encoding, _archiveGroup, _installTags, _shared_game_dir);

        await DataFileComplete();
        
        WriteProductDB(_game_dir, _version, _productConfig, _buildConfig);
        WritePatchResult(_game_dir);
        WriteLauncherDB(_game_dir);
        
        await DownloadLauncher(_productConfig, _branch, _game_dir);
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
                    data = await DownloadFileFromIndex(indexEntry, cdn, cdnConfig);
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

    private static async Task<byte[]?> DownloadFileFromIndex(ArchiveIndex.IndexEntry indexEntry, CDN? cdn, CDNConfig? cdnConfig)
    {
        var hosts = cdn?.Hosts;
        if (hosts == null) return null;
        var archive = cdnConfig?.Archives?[indexEntry.archiveIndex];
        if (archive == null) return null;
        
        foreach (var cdnURL in hosts)
        {
            var url = $@"http://{cdnURL}/{cdn?.Path}/data/{archive.Value.UrlString}";
            var dataFilePath = Path.Combine(cache_dir, $"{archive.Value.KeyString!}_{indexEntry.offset}_{indexEntry.size}.data");
            if (File.Exists(dataFilePath))
            {
                return await File.ReadAllBytesAsync(dataFilePath);
            }
            else
            {
                var encryptedData = await Utils.GetDataFromURL(url, (int)indexEntry.offset, (int)indexEntry.size);
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
                
                return decryptedData;
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
            
            return data;
        }
        
        return null;
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

        foreach (var (key, idx) in idxMap)
        {
            if (File.Exists(idx.Path))
                File.Delete(idx.Path);

            idx.Write();
        }
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

    private async Task DownloadAndWriteFile(Hash eKey, CDN? cdn, CDNConfig? cdnConfig, ConcurrentDictionary<Hash, ArchiveIndex.IndexEntry>? archiveGroup, ulong size)
    {
        var writeSize = size + 30;
        var bucket = cascGetBucketIndex(eKey);

        if (idxMap.TryGetValue(bucket, out var idx))
        {
            if (Working_Stream == null)
                Working_Stream = new MemoryStream();
            
            if (Working_Offset + (int)writeSize > DATA_TOTAL_SIZE_MAXIMUM)
            {
                await DataFileComplete();

                Working_Offset = DATA_OFFSET_START;

                Working_Data++;
            }

            var idxEntry = idx.Add(eKey, Working_Data, Working_Offset, writeSize);
            
            await WriteDataEntry(idxEntry, cdn, cdnConfig, archiveGroup);
            Working_Offset += (int)writeSize;
        }
    }

    private async Task DataFileComplete()
    {
        if (Working_Stream != null)
        {
            await using var bw = new BinaryWriter(Working_Stream, System.Text.Encoding.UTF8, leaveOpen: true);
            foreach (var (_, idx) in idxMap)
            {
                casReconstructionHeaderSerialize(bw, idx, Working_Data);  // Write the initial header
            }

            var dataPath = Path.Combine(gameDataDir, $"data.{Working_Data:D3}");
            var fileStream = File.Create(dataPath);
            Working_Stream.WriteTo(fileStream);
            fileStream.Close();
            Working_Stream.SetLength(0);
        }
    }

    private async Task WriteDataEntry(IDX.Entry idxEntry, CDN? cdn, CDNConfig? cdnConfig, ConcurrentDictionary<Hash, ArchiveIndex.IndexEntry>? archiveGroup)
    {
        var offset = idxEntry.Offset;
        var key = idxEntry.Key;
        var archiveID = idxEntry.ArchiveID;

        byte[]? data = null;

        if (archiveGroup != null && archiveGroup.TryGetValue(key, out var indexEntry))
        {
            try
            {
                data = await DownloadFileFromIndex(indexEntry, cdn, cdnConfig);
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

        if (data == null) return;

        // Ensure we're not overwriting stream position by reusing the BinaryWriter
        await using var bws = new BinaryWriter(Working_Stream, System.Text.Encoding.UTF8, leaveOpen: true);
        var header = new casReconstructionHeader()
        {
            BLTEHash = key.Key.Reverse().ToArray(),
            size = idxEntry.Size,
            channel = casIndexChannel.Data,
        };
        
        bws.Seek(offset, SeekOrigin.Begin);
        header.Write(bws, (ushort)archiveID, (uint)offset);
        bws.Write(data);
    }

    enum casIndexChannel : byte
    {
        Data = 0x0,
        Meta = 0x1,
    }
    
    struct casReconstructionHeader
    {
        public byte[] BLTEHash;
        public uint size;
        public casIndexChannel channel;
        private uint checksumA;
        private uint checksumB;

        public void Write(BinaryWriter bw, ushort archiveIndex, uint archiveOffset)
        {
            using var headerMs = new MemoryStream();
            using var headerBw = new BinaryWriter(headerMs);
            headerBw.Write(BLTEHash);
            headerBw.Write(size);
            headerBw.Write((byte)channel);
            headerBw.Write((byte)0);

            headerBw.Flush();
            headerMs.Position = 0;
            var checksumA = HashAlgo.HashLittle(headerMs.ToArray(), 0x16, 0x3D6BE971);
            headerMs.Position = 0x16;
            headerBw.Write(checksumA);
            
            headerBw.Flush();
            headerMs.Position = 0;
            var checksumB = HashAlgo.CalculateChecksum(headerMs.ToArray(), archiveIndex, archiveOffset);
            headerMs.Position = 0x1A;
            headerBw.Write(checksumB);

            bw.Write(headerMs.ToArray());
        }
    }
    
    private void casReconstructionHeaderSerialize(BinaryWriter bw, IDX idx, int dataNumber)
    {
        var container = new CasContainerIndex()
        {
            BaseDir = "World of Warcraft",
            BindMode = true,
            MaxSize = 30
        };

        var (Result, SegmentHeaderKeys, ShortSegmentHeaderKeys) = container.GenerateSegmentHeaderKeys((uint)dataNumber);
        
        for (var i = 0; i < 16; i++)
        {
            var reversedSegmentHeaderKey = SegmentHeaderKeys[i].Reverse().ToArray();
            var idxEntry = idx.Add(new Hash(SegmentHeaderKeys[i]), Working_Data, i * 30, 30);
            
            var header = new casReconstructionHeader()
            {
                BLTEHash = reversedSegmentHeaderKey,
                size = 30,
                channel = casIndexChannel.Meta,
            };
            header.Write(bw, (ushort)dataNumber, (uint)(0x1e * i));
        }
    }

    private static byte cascGetBucketIndex(Hash key)
    {
        var k = key.Key;
        var i = k[0] ^ k[1] ^ k[2] ^ k[3] ^ k[4] ^ k[5] ^ k[6] ^ k[7] ^ k[8];
        return (byte)((i & 0xf) ^ (i >> 4));
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
    
    private async Task DownloadLauncher(ProductConfig? productConfig, string? branch, string? game_dir)
    {
        if (productConfig == null)
            return;

        var tags = new List<string>();
        tags.AddRange(productConfig.platform.win.config.tags);  // Eg. Add "Windows" tag
        
        var product_tag = productConfig.all.config.launcher_install_info.product_tag;
        var bootstrapper_product = productConfig.all.config.launcher_install_info.bootstrapper_product; // "bts"
        var bootstrapper_branch = productConfig.all.config.launcher_install_info.bootstrapper_branch;   // "launcher"
        tags.Add(product_tag);  // Eg. Add "wow" tag
        
        var data_dir = Path.Combine(game_dir ?? "", ".battle.net");
        if (!Directory.Exists(data_dir))
            Directory.CreateDirectory(data_dir);
        
        //var version = "http://us.patch.battle.net:1119/bts/versions";
        var launcher_version = await Version.GetVersion(bootstrapper_product, bootstrapper_branch);
        launcher_version.LogInfo();
        var launcher_cdn = await CDN.GetCDN(bootstrapper_product, branch);
        launcher_cdn.LogInfo();
        var launcher_buildConfig = await BuildConfig.GetBuildConfig(launcher_cdn, launcher_version.BuildConfigHash, data_dir);
        launcher_buildConfig.LogInfo();
        var launcher_cdnConfig = await CDNConfig.GetConfig(launcher_cdn, launcher_version.CdnConfigHash, data_dir);
        launcher_cdnConfig.LogInfo();
        var launcher_archiveGroup = await ProcessIndices(
            launcher_cdn, launcher_cdnConfig, launcher_cdnConfig.Archives, launcher_cdnConfig.ArchiveGroup,
            data_dir, "data", 6);
        var launcher_encoding = await Encoding.GetEncoding(launcher_cdn, launcher_buildConfig.Encoding[1]);
        launcher_encoding.LogInfo();
        launcher_encoding.Dump("launcher_encoding.txt");
        var launcher_fileIndex = await FileIndex.GetDataIndex(launcher_cdn, launcher_cdnConfig.FileIndex, "data", data_dir);
        var launcher_install = await InstallManifest.GetInstall(launcher_cdn, launcher_buildConfig.Install[1]);
        launcher_install?.LogInfo();
        //launcher_install.Dump("launcher_install.txt");
        await ProcessInstall(launcher_install, launcher_cdn, launcher_cdnConfig, launcher_encoding, launcher_archiveGroup, tags, game_dir);
        var launcher_download = await DownloadManifest.GetDownload(launcher_cdn, launcher_buildConfig.Download[1]);
        launcher_download.LogInfo();
        await ProcessDownload(launcher_download, launcher_cdn, launcher_cdnConfig, launcher_encoding, launcher_archiveGroup, null, tags, data_dir);
        File.SetAttributes(data_dir, FileAttributes.Hidden);
    }

    private void TestGenerateSegmentHeaderKeys()
    {
        /*
        using var headerMs = new MemoryStream();
        using var headerBw = new BinaryWriter(headerMs);
        headerBw.Write(new byte[] {0x58, 0xEA, 0xCB, 0x4A, 0x76, 0x57, 0xDF, 0x28, 0x78, 0xD2, 0xAE, 0x15, 0x21, 0x00, 0x00, 0x06});
        headerBw.Write(30);
        headerBw.Write((byte)1);
        headerBw.Write((byte)0);

        headerBw.Flush();
        headerMs.Position = 0;
        var checksumA = HashAlgo.HashLittle(headerMs.ToArray(), 0x16, 0x3D6BE971);
        headerMs.Position = 0x16;
        headerBw.Write(checksumA);
            
        headerBw.Flush();
        headerMs.Position = 0;
        var checksumB = HashAlgo.CalculateChecksum(headerMs.ToArray(), 0, 0);
        
        AnsiConsole.WriteLine(checksumA + " " + checksumB);
        */
        //CasContainerIndex casContainerIndex = new CasContainerIndex("World of Warcraft", 30, CasDynamicOpenMode.Reconstruction);
        //casContainerIndex.GenerateSegmentHeaderKeys(1, out var shortSegmentKeys, out var keys);

        //foreach (var key in keys)
        //{
        //    var reversed = key.Data.Reverse().ToArray();
        //    AnsiConsole.WriteLine(reversed.ToHexString());
        //}
        
        CasContainerIndex container = new CasContainerIndex()
        {
            BaseDir = "D:\\Games\\World of Warcraft\\Data\\",
            BindMode = true,
            MaxSize = 30
        };
        (CasContainerIndex.CasResult Result, byte[][] SegmentHeaderKeys, byte[][] ShortSegmentHeaderKeys) = container.GenerateSegmentHeaderKeys(0);
        foreach (var key in SegmentHeaderKeys)
        {
            var reversed = key.Reverse().ToArray();
            AnsiConsole.WriteLine(reversed.ToHexString());
        }
        
    }
}