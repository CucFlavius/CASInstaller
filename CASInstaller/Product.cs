using System.Collections.Concurrent;
using Google.Protobuf;
using ProtoDatabase;
using Spectre.Console;

namespace CASInstaller;

public partial class Product
{
    const string cache_dir = "cache";

    readonly string _product;
    readonly string _branch;
    readonly InstallSettings _installSettings;
    string? _installPath;
    CDN? _cdn;
    Version? _version;
    public ProductConfig? _productConfig;
    public string? _game_dir;
    public string? _shared_game_dir;
    public string? _data_dir;
    public List<string>? _installTags;
    BuildConfig? _buildConfig;
    CDNConfig? _cdnConfig;
    FileIndex? _fileIndex;
    FileIndex? _patchFileIndex;
    ConcurrentDictionary<Hash, ArchiveIndex.IndexEntry>? _archiveGroup;
    ConcurrentDictionary<Hash, ArchiveIndex.IndexEntry>? _patchGroup;
    Encoding? _encoding;
    DownloadManifest? _download;
    InstallManifest? _install;
    Dictionary<byte, IDX>? idxMap;
    string? _gameDataDir;
    BuildInfo? _buildInfo;
    List<byte[][]>? segmentHeaderKeyList;
    Data? Working_Data { get; set; }

    public Product(string product, string branch = "us", InstallSettings installSettings = null!)
    {
        _product = product;
        _branch = branch;
        _installSettings = installSettings;
    }

    public async Task Install(string installPath)
    {
        if (!Directory.Exists(cache_dir))
            Directory.CreateDirectory(cache_dir);
        var cacheIndicesDir = Path.Combine(cache_dir, "indices");
        if (!Directory.Exists(cacheIndicesDir))
            Directory.CreateDirectory(cacheIndicesDir);

        _installPath = installPath;

        BLTE.BLTEStream.ThrowOnMissingDecryptionKey = false;
        KeyService.LoadKeys();

        if (string.IsNullOrEmpty(_installSettings.LocalCDNPath))
            _cdn = await CDNOnline.GetCDN(_product, _branch);
        else
            _cdn = new CDNLocal(_product, _installSettings.LocalCDNPath);

        if (_cdn != null)
        {
            if (_installSettings.OverrideHosts != null)
                _cdn.Hosts = _installSettings.OverrideHosts.Split(' ');
            _cdn.LogInfo();

            _version = await Version.GetVersion(_product, _branch);
            if (_version == null)
            {
                AnsiConsole.MarkupLine($"[red]Version not found for product: {_product} branch: {_branch}[/]");
                return;
            }

            if (_installSettings.OverrideCDNConfig != null)
                _version.CdnConfigHash = new Hash(_installSettings.OverrideCDNConfig);
            else if (_installSettings.OverrideCDNConfigFile != null)
                _version.CdnConfigHash = new Hash(Path.GetFileName(_installSettings.OverrideCDNConfigFile));
            if (_installSettings.OverrideBuildConfig != null)
                _version.BuildConfigHash = new Hash(_installSettings.OverrideBuildConfig);
            else if (_installSettings.OverrideBuildConfigFile != null)
                _version.BuildConfigHash = new Hash(Path.GetFileName(_installSettings.OverrideBuildConfigFile));
            //_version?.LogInfo();

            // Fetch and load keyring if available
            if (_version.KeyRing.Key != null && _version.KeyRing.Key.Length == 16 && !_version.KeyRing.IsEmpty())
            {
                var keyringData = await _cdn.GetConfig(_version.KeyRing);
                if (keyringData != null)
                {
                    var keyringText = System.Text.Encoding.UTF8.GetString(keyringData);
                    KeyService.LoadKeyring(keyringText);
                }
            }

            _productConfig = await ProductConfig.GetProductConfig(_cdn, _version.ProductConfigHash);
            _productConfig?.Dump("productConfig.json");
            ProcessProductConfig(_productConfig, _installPath);

            if (!string.IsNullOrEmpty(_productConfig?.all.config.decryption_key_name))
                ArmadilloCrypt.Init(_productConfig.all.config.decryption_key_name);

            if (_installSettings.OverrideBuildConfigFile != null)
                _buildConfig = new BuildConfig(await File.ReadAllBytesAsync(_installSettings.OverrideBuildConfigFile));
            else
                _buildConfig = await BuildConfig.GetBuildConfig(_cdn, _version.BuildConfigHash, _data_dir);
            //_buildConfig?.LogInfo();

            if (_installSettings.OverrideCDNConfigFile != null)
                _cdnConfig = new CDNConfig(System.Text.Encoding.UTF8.GetString(await File.ReadAllBytesAsync(_installSettings.OverrideCDNConfigFile)));
            else
                _cdnConfig = await CDNConfig.GetConfig(_cdn, _version.CdnConfigHash, _data_dir);

            // Always save config files to install directory for TACT initialization
            if (_data_dir != null)
            {
                await SaveConfigToInstall(_version.BuildConfigHash, _installSettings.OverrideBuildConfigFile, _data_dir);
                await SaveConfigToInstall(_version.CdnConfigHash, _installSettings.OverrideCDNConfigFile, _data_dir);
            }
            //_cdnConfig?.LogInfo();

            if (_cdnConfig != null)
            {
                _fileIndex = await FileIndex.GetDataIndex(_cdn, _cdnConfig.Value.FileIndex, "data", _data_dir);
                //_fileIndex?.Dump("fileindex.txt");

                _patchFileIndex = FileIndex.GetDataIndex(_cdn, _cdnConfig.Value.PatchFileIndex, "patch", _data_dir).Result;
                //_patchFileIndex?.Dump("patchfileindex.txt");

                _archiveGroup = await ProcessIndices(_cdn, _cdnConfig, _cdnConfig.Value.Archives, _cdnConfig.Value.ArchiveGroup,
                    _data_dir, "data", 6);
                _patchGroup = await ProcessIndices(_cdn, _cdnConfig, _cdnConfig.Value.PatchArchives,
                    _cdnConfig.Value.PatchArchiveGroup, _data_dir, "patch", 5);
            }

            if (_buildConfig != null)
            {
                _encoding = await Encoding.GetEncoding(_cdn, _buildConfig.Value.Encoding[^1], 0, true);
                //_encoding?.LogInfo();
                //_encoding?.Dump("encoding.txt");

                _download = await DownloadManifest.GetDownload(_cdn, _buildConfig.Value.Download[^1]);
                _download?.Dump($"download_manifest_analysis_{_product}.txt");
                //_download?.LogInfo();

                _install = await InstallManifest.GetInstall(_cdn, _buildConfig.Value.Install[^1]);
                _install?.Dump("install.txt");
                //_install?.LogInfo();
            }

            // Download data //
            _gameDataDir = Path.Combine(_data_dir ?? "", "data");
            if (!Directory.Exists(_gameDataDir))
                Directory.CreateDirectory(_gameDataDir);

            BuildIDXMap(_gameDataDir);

            // Collect all entries to write: special entries (sorted by key, patch-index last) first, then download entries
            var allWriteEntries = new List<(Hash eKey, ulong size, bool truncateKey)>();

            // Special entries (full 16-byte key, no truncation) - sorted by eKey, deduplicated
            var specialEntries = new List<(Hash eKey, ulong size, bool truncateKey)>();
            var specialKeysSeen = new HashSet<Hash>();
            (Hash eKey, ulong size, bool truncateKey)? patchIndexEntry = null;

            // Add encoding, download, install (sorted among VFS entries)
            void AddSpecialEntry(Hash[]? keys, string[]? sizes)
            {
                if (keys == null || keys.Length == 0 || sizes == null || sizes.Length == 0) return;
                var eKey = keys[^1];
                if (specialKeysSeen.Add(eKey))
                    specialEntries.Add((eKey, (ulong)int.Parse(sizes[^1]), false));
            }
            AddSpecialEntry(_buildConfig?.Download, _buildConfig?.DownloadSize);
            AddSpecialEntry(_buildConfig?.Install, _buildConfig?.InstallSize);
            AddSpecialEntry(_buildConfig?.Encoding, _buildConfig?.EncodingSize);

            // Patch-index is written LAST in the sorted section (not at its key-sorted position)
            if (_buildConfig?.PatchIndex != null && _buildConfig?.PatchIndexSize != null
                && _buildConfig.Value.PatchIndex.Length >= 2 && _buildConfig.Value.PatchIndexSize.Length >= 2)
            {
                var piKey = new Hash(_buildConfig.Value.PatchIndex[1]);
                patchIndexEntry = (piKey, (ulong)int.Parse(_buildConfig.Value.PatchIndexSize[1]), false);
                specialKeysSeen.Add(piKey);
            }

            // VFS entries (deduplicated by eKey)
            if (_buildConfig?.VfsEntries != null)
            {
                foreach (var vfs in _buildConfig.Value.VfsEntries)
                {
                    if (specialKeysSeen.Add(vfs.EKey))
                        specialEntries.Add((vfs.EKey, (ulong)vfs.EncodedSize, false));
                }
            }

            // Sort by eKey ascending
            specialEntries.Sort((a, b) =>
            {
                var ka = a.eKey.Key;
                var kb = b.eKey.Key;
                for (int i = 0; i < Math.Min(ka.Length, kb.Length); i++)
                {
                    int cmp = ka[i].CompareTo(kb[i]);
                    if (cmp != 0) return cmp;
                }
                return ka.Length.CompareTo(kb.Length);
            });
            allWriteEntries.AddRange(specialEntries);

            // Append patch-index at the end of sorted section
            if (patchIndexEntry != null)
                allWriteEntries.Add(patchIndexEntry.Value);

            // Build artifact exclusion set from install manifest eKeys
            var artifactKeys = new HashSet<Hash>();
            if (_install != null && _encoding != null)
            {
                var installTagQuery = TagFilter.BuildTagQuery(_installTags, "enUS",
                    _branch.ToUpperInvariant() switch { "US" => "US", "EU" => "EU", "KR" => "KR", "TW" => "TW", "CN" => "CN", _ => "US" });
                var installFilterBitmap = TagFilter.FilterInstallEntries(_install, installTagQuery);
                for (var i = 0; i < _install.entries.Length; i++)
                {
                    if (!installFilterBitmap[i]) continue;
                    if (_encoding.contentEntries.TryGetValue(_install.entries[i].contentHash, out var ce))
                        artifactKeys.Add(ce.eKeys[0]);
                }
            }

            // Download entries (truncated 9-byte key)
            // Combined tag filter, sorted by (CDN config index, archiveOffset) with loose entries interleaved
            if (_download != null)
            {
                var region = _branch.ToUpperInvariant() switch
                {
                    "US" => "US", "EU" => "EU", "KR" => "KR", "TW" => "TW", "CN" => "CN", _ => "US"
                };
                var tagQuery = TagFilter.BuildTagQuery(_installTags, "enUS", region);
                var downloadBitmap = TagFilter.FilterDownloadEntries(_download, tagQuery);

                // Build exclusion sets
                var excludeKeys = new HashSet<Hash>(specialKeysSeen);
                foreach (var ak in artifactKeys) excludeKeys.Add(ak);

                // Collect entries per priority
                var priorityGroups = new SortedDictionary<int, List<DownloadManifest.DownloadEntry>>();
                for (var i = 0; i < _download.entries.Length; i++)
                {
                    var entry = _download.entries[i];
                    if (entry.priority is not (0 or 1 or 2)) continue;
                    if (!downloadBitmap[i]) continue;
                    if (excludeKeys.Contains(entry.eKey)) continue;
                    if (entry.size == 0) continue;

                    var prio = (int)entry.priority;
                    if (!priorityGroups.ContainsKey(prio))
                        priorityGroups[prio] = new List<DownloadManifest.DownloadEntry>();
                    priorityGroups[prio].Add(entry);
                }

                AnsiConsole.MarkupLine($"[yellow]Download entries by priority: {string.Join(", ", priorityGroups.Select(kv => $"p{kv.Key}={kv.Value.Count}"))} total={priorityGroups.Sum(kv => kv.Value.Count)}[/]");

                // Precompute: sorted archive hashes for binary search → CDN config index mapping
                var archives = _cdnConfig!.Value.Archives!;
                var sortedArchivesList = archives
                    .Select((h, i) => (hash: h, cdnIdx: i))
                    .OrderBy(x => x.hash)
                    .ToArray();
                var sortedArchiveHashes = sortedArchivesList.Select(x => x.hash).ToArray();

                // Sort by (CDN config index, offset) with loose entries interleaved
                foreach (var (prio, entries) in priorityGroups)
                {
                    var sortedEntries = new List<(DownloadManifest.DownloadEntry entry, int groupPos, bool isLoose, uint archOff)>();
                    var seenKeys = new HashSet<Hash>();

                    foreach (var entry in entries)
                    {
                        if (!seenKeys.Add(entry.eKey)) continue;
                        if (_archiveGroup != null && _archiveGroup.TryGetValue(entry.eKey, out var idx))
                        {
                            sortedEntries.Add((entry, idx.archiveIndex, false, idx.offset));
                        }
                        else
                        {
                            var insertPos = Array.BinarySearch(sortedArchiveHashes, entry.eKey);
                            if (insertPos < 0) insertPos = ~insertPos - 1;
                            var precedingCdnIdx = insertPos >= 0 ? sortedArchivesList[insertPos].cdnIdx : -1;
                            sortedEntries.Add((entry, precedingCdnIdx, true, 0));
                        }
                    }

                    sortedEntries.Sort((a, b) =>
                    {
                        if (a.groupPos != b.groupPos) return a.groupPos.CompareTo(b.groupPos);
                        if (a.isLoose != b.isLoose) return a.isLoose ? 1 : -1;
                        if (!a.isLoose)
                            return a.archOff.CompareTo(b.archOff);
                        return a.entry.eKey.CompareTo(b.entry.eKey);
                    });

                    foreach (var (entry, _, _, _) in sortedEntries)
                        allWriteEntries.Add((entry.eKey, entry.size, true));
                }

            }

            // Phase 1: Parallel pre-download to cache
            await AnsiConsole.Progress().StartAsync(async ctx =>
            {
                var task1 = ctx.AddTask($"[green]Pre-downloading {allWriteEntries.Count} files[/]");
                task1.MaxValue = allWriteEntries.Count;
                await Parallel.ForEachAsync(allWriteEntries,
                    new ParallelOptions { MaxDegreeOfParallelism = 20 },
                    async (entry, ct) =>
                    {
                        await Data.PreDownloadToCache(entry.eKey, _cdn, _cdnConfig, _archiveGroup);
                        task1.Increment(1);
                    });
            });

            // Phase 2: Sequential write with bin-packing
            // When an entry doesn't fit in the current data file, defer it.
            // After trying all entries, finalize the data file and repeat with deferred entries.
            await AnsiConsole.Progress().StartAsync(async ctx =>
            {
                var task1 = ctx.AddTask($"[green]Writing {allWriteEntries.Count} files to data archives[/]");
                task1.MaxValue = allWriteEntries.Count;
                var pending = allWriteEntries;
                while (pending.Count > 0)
                {
                    var deferred = new List<(Hash eKey, ulong size, bool truncateKey)>();
                    foreach (var entry in pending)
                    {
                        var writeSize = (long)entry.size + 30;

                        // Ensure Working_Data exists
                        if (Working_Data == null)
                        {
                            segmentHeaderKeyList ??= [];
                            Working_Data = new Data(segmentHeaderKeyList.Count, out var shk, _baseDir);
                            segmentHeaderKeyList.Add(shk);
                        }

                        if (!Working_Data.CanWrite(writeSize))
                        {
                            deferred.Add(entry);
                            continue;
                        }

                        await DownloadAndWriteFile(entry.eKey, _cdn, _cdnConfig, _archiveGroup, entry.size, entry.truncateKey);
                        task1.Increment(1);
                    }

                    if (deferred.Count > 0)
                    {
                        // Finalize current data file, next iteration creates a new one
                        if (Working_Data != null && _gameDataDir != null)
                        {
                            Working_Data.Finalize(_gameDataDir);
                            Working_Data = null;
                            GC.Collect();
                        }
                    }
                    pending = deferred;
                }
            });
            AnsiConsole.MarkupLine($"[bold green]Write complete: {allWriteEntries.Count} files[/]");
            await ProcessInstall(_install, _cdn, _cdnConfig, _encoding, _archiveGroup, _installTags, _shared_game_dir, _branch);
        }

        if (_gameDataDir != null) Working_Data?.Finalize(_gameDataDir);

        WriteIDXMap();
        WriteProductIDX();
        WriteShmemFiles();
        WriteEcacheDirectories();
        WriteExtractDirectory();
        WriteResidencyFiles();
        CleanupRepairMarker();

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

    void ProcessProductConfig(ProductConfig? productConfig, string installPath)
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
        {
            _game_dir = Path.Combine(installPath, productConfig.all.config.form.game_dir.dirname);
        }
        if (!Directory.Exists(_game_dir))
            Directory.CreateDirectory(_game_dir!);
        if (productConfig.all?.config?.shared_container_default_subfolder != null)
            _shared_game_dir = Path.Combine(_game_dir!, productConfig.all.config.shared_container_default_subfolder);
        if (_shared_game_dir == null)
            _shared_game_dir = _game_dir;
        if (!Directory.Exists(_shared_game_dir))
            Directory.CreateDirectory(_shared_game_dir!);

        if (productConfig?.all?.config?.data_dir != null)
            _data_dir = Path.Combine(_game_dir!, productConfig.all.config.data_dir);
        if (!Directory.Exists(_data_dir))
            Directory.CreateDirectory(_data_dir!);

        // Agent passes the full absolute normalized path to the CASC data directory
        // e.g., "d:/Games/_classic_era_/Data/data" (forward slashes, lowercase drive letter)
        _baseDir = NormalizePath(Path.Combine(_data_dir!, "data"));
    }

    private static async Task<ConcurrentDictionary<Hash, ArchiveIndex.IndexEntry>?> ProcessIndices(
        CDN? cdn, CDNConfig? cdnConfig, Hash[]? indices, Hash groupKey, string? data_dir,
        string pathType, byte offsetBytes, bool overrideGroup = false)
    {
        if (cdn == null || cdnConfig == null || groupKey.IsEmpty())
            return null;

        var indexGroup = new ConcurrentDictionary<Hash, ArchiveIndex.IndexEntry>();

        var saveDir = Path.Combine(data_dir ?? "", "indices");
        var groupPath = Path.Combine(saveDir, groupKey.KeyString + ".index");
        var cacheGroupPath = Path.Combine("cache", "indices", groupKey.KeyString + ".index");

        // Check both install-local and persistent cache for group index
        var existingGroupPath = File.Exists(groupPath) ? groupPath :
                                File.Exists(cacheGroupPath) ? cacheGroupPath : null;

        if (existingGroupPath != null && !overrideGroup)
        {
            // Load existing archive group index
            _ = await ArchiveIndex.GetDataIndex(cdn, groupKey, pathType, data_dir, indexGroup, (ushort)0);
        }
        else
        {
            if (indices != null)
            {
                var length = indices.Length;
                await AnsiConsole.Progress().StartAsync(async ctx =>
                {
                    var task1 = ctx.AddTask($"[green]Processing {length} indices[/]");
                    task1.MaxValue = length;
                    await Parallel.ForEachAsync(Enumerable.Range(0, length),
                        new ParallelOptions { MaxDegreeOfParallelism = 20 },
                        async (i, ct) =>
                        {
                            var indexKey = indices[i];
                            _ = await ArchiveIndex.GetDataIndex(cdn, indexKey, pathType, data_dir, indexGroup, (ushort)i);
                            task1.Increment(1);
                        });
                });
            }

            // Save the archive group index to both locations
            if (data_dir != null)
                ArchiveIndex.GenerateIndexGroupFile(indexGroup, groupPath, offsetBytes);
            ArchiveIndex.GenerateIndexGroupFile(indexGroup, cacheGroupPath, offsetBytes);
        }

        // Ensure all individual archive index files are saved to the install directory
        if (indices != null && data_dir != null)
        {
            if (!Directory.Exists(saveDir))
                Directory.CreateDirectory(saveDir);

            var cacheDir = Path.Combine("cache", "indices");
            var missingCount = 0;
            foreach (var indexKey in indices)
            {
                var savePath = Path.Combine(saveDir, indexKey + ".index");
                if (File.Exists(savePath)) continue;

                // Try copying from persistent cache
                var cachePath = Path.Combine(cacheDir, indexKey + ".index");
                if (File.Exists(cachePath))
                {
                    File.Copy(cachePath, savePath);
                    continue;
                }

                missingCount++;
            }

            // Download any that are missing from both locations
            if (missingCount > 0)
            {
                await AnsiConsole.Progress().StartAsync(async ctx =>
                {
                    var task1 = ctx.AddTask($"[green]Saving {missingCount} missing archive indices[/]");
                    task1.MaxValue = missingCount;
                    await Parallel.ForEachAsync(Enumerable.Range(0, indices.Length),
                        new ParallelOptions { MaxDegreeOfParallelism = 20 },
                        async (i, ct) =>
                        {
                            var indexKey = indices[i];
                            var savePath = Path.Combine(saveDir, indexKey + ".index");
                            if (File.Exists(savePath)) return;

                            // Download and save
                            _ = await ArchiveIndex.GetDataIndex(cdn, indexKey, pathType, data_dir, null, (ushort)i);
                            task1.Increment(1);
                        });
                });
            }
        }

        return indexGroup;
    }

    private static async Task ProcessInstall(
        InstallManifest? install, CDN? cdn, CDNConfig? cdnConfig, Encoding? encoding,
        ConcurrentDictionary<Hash, ArchiveIndex.IndexEntry>? archiveGroup, List<string>? tags,
        string? shared_game_dir, string branch = "us", bool overrideFiles = false)
    {
        if (install == null || cdn == null || encoding == null || shared_game_dir == null)
            return;

        tags ??= [];

        var region = branch.ToUpperInvariant() switch
        {
            "US" => "US",
            "EU" => "EU",
            "KR" => "KR",
            "TW" => "TW",
            "CN" => "CN",
            _ => "US"
        };

        var tagQuery = TagFilter.BuildTagQuery(tags, "enUS", region);
        var filterBitmap = TagFilter.FilterInstallEntries(install, tagQuery);

        var tagFilteredEntries = new List<InstallManifest.InstallFileEntry>();
        for (var i = 0; i < install.entries.Length; i++)
        {
            if (filterBitmap[i])
                tagFilteredEntries.Add(install.entries[i]);
        }

        // Resolve content hash → eKey for all entries first
        var resolvedEntries = new List<(InstallManifest.InstallFileEntry entry, Hash eKey)>();
        foreach (var installEntry in tagFilteredEntries)
        {
            if (!encoding.contentEntries.TryGetValue(installEntry.contentHash, out var contentEntry)) continue;
            var key = contentEntry.eKeys[0];

            var filePath = Path.Combine(shared_game_dir, installEntry.name);
            if (File.Exists(filePath) && !overrideFiles)
                continue;

            resolvedEntries.Add((installEntry, key));
        }

        // Phase 1: Parallel pre-download to cache
        await AnsiConsole.Progress().StartAsync(async ctx =>
        {
            var task1 = ctx.AddTask($"[green]Pre-downloading {resolvedEntries.Count} install files[/]");
            task1.MaxValue = resolvedEntries.Count;

            await Parallel.ForEachAsync(resolvedEntries,
                new ParallelOptions { MaxDegreeOfParallelism = 20 },
                async (item, ct) =>
                {
                    await Data.PreDownloadToCache(item.eKey, cdn, cdnConfig, archiveGroup);
                    task1.Increment(1);
                });
        });

        // Phase 2: Sequential BLTE decode + file extraction (reads from cache)
        foreach (var (installEntry, key) in resolvedEntries)
        {
            var filePath = Path.Combine(shared_game_dir, installEntry.name);

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

            try
            {
                // Decode BLTE, install files must be extracted
                using var ms = new MemoryStream(data);
                await using var blte = new BLTE.BLTEStream(ms, default);
                using var fso = new MemoryStream();
                await blte.CopyToAsync(fso);
                data = fso.ToArray();
            }
            catch (Exception e)
            {
                AnsiConsole.WriteException(e);
            }

            var dirPath = Path.GetDirectoryName(filePath) ?? "";
            if (!Directory.Exists(dirPath))
                Directory.CreateDirectory(dirPath);

            await File.WriteAllBytesAsync(filePath, data);
        }
    }

    async Task ProcessDownload(
        DownloadManifest? download, CDN? cdn, CDNConfig? cdnConfig, Encoding? encoding,
        ConcurrentDictionary<Hash, ArchiveIndex.IndexEntry>? archiveGroup,
        ConcurrentDictionary<Hash, ArchiveIndex.IndexEntry>? patchGroup, List<string>? tags,
        string? data_dir, HashSet<Hash>? artifactKeys = null, bool overrideFiles = false)
    {
        if (download == null)
            return;

        tags ??= [];

        // Derive region from branch
        var region = _branch.ToUpperInvariant() switch
        {
            "US" => "US",
            "EU" => "EU",
            "KR" => "KR",
            "TW" => "TW",
            "CN" => "CN",
            _ => "US"
        };

        var tagQuery = TagFilter.BuildTagQuery(tags, "enUS", region);
        var filterBitmap = TagFilter.FilterDownloadEntries(download, tagQuery);

        var downloadEntries = new List<DownloadManifest.DownloadEntry>();
        for (var i = 0; i < download.entries.Length; i++)
        {
            if (!filterBitmap[i] || download.entries[i].priority is not (0 or 1 or 2))
                continue;
            // Agent artifact exclusion: skip entries already written as build config artifacts
            if (artifactKeys != null && artifactKeys.Contains(download.entries[i].eKey))
                continue;
            // Agent skips entries with zero compressed size (nothing to download)
            if (download.entries[i].size == 0)
                continue;
            downloadEntries.Add(download.entries[i]);
        }

        // Agent processes download entries grouped by priority (0, 1, 2) in manifest order within each group
        downloadEntries = downloadEntries.OrderBy(e => e.priority).ToList();

        // Phase 1: Parallel pre-download to cache
        await AnsiConsole.Progress().StartAsync(async ctx =>
        {
            var task1 = ctx.AddTask($"[green]Pre-downloading {downloadEntries.Count} files[/]");
            task1.MaxValue = downloadEntries.Count;

            await Parallel.ForEachAsync(downloadEntries,
                new ParallelOptions { MaxDegreeOfParallelism = 20 },
                async (entry, ct) =>
                {
                    await Data.PreDownloadToCache(entry.eKey, cdn, cdnConfig, archiveGroup);
                    task1.Increment(1);
                });
        });

        // Phase 2: Sequential write from cache (fast, local I/O only)
        await AnsiConsole.Progress().StartAsync(async ctx =>
        {
            var task1 = ctx.AddTask($"[green]Writing {downloadEntries.Count} files to data archives[/]");
            task1.MaxValue = downloadEntries.Count;

            foreach (var entry in downloadEntries)
            {
                await DownloadAndWriteFile(entry.eKey, cdn, cdnConfig, archiveGroup, entry.size, truncateKey: true);
                task1.Increment(1);
            }
        });
        AnsiConsole.MarkupLine($"[bold green]Download complete: {downloadEntries.Count} files[/]");
    }

    void BuildIDXMap(string dataDir)
    {
        const byte idxN = 1;        // Only need to increase this number if we update the game
        idxMap = new Dictionary<byte, IDX>();
        for (var i = 0; i < 16; i++)
        {
            var bucket = (byte)i;
            var path = Path.Combine(dataDir, $"{bucket:x2}000000{idxN:x2}.idx");
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
                // Segment header keys use bucket mode +1 for both generation and lookup,
                // so use the generation bucket index directly (not cascGetBucketIndex which is mode 0)
                var bucket = (byte)i;
                if (idxMap.TryGetValue(bucket, out var sidx))
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

    void WriteProductIDX()
    {
        if (_data_dir == null) return;
        var productDir = Path.Combine(_data_dir, _product);
        IDXV8.WriteProductIndex(productDir);
    }

    void WriteShmemFiles()
    {
        if (_data_dir == null) return;

        var bucketVersions = new uint[16];
        Array.Fill(bucketVersions, 1u); // We write version 1 IDX files

        // Data/data/shmem
        var gameDataDir = Path.Combine(_data_dir, "data");
        var gameDataPath = NormalizePath(gameDataDir);
        var dataFileCount = 0u;
        if (Directory.Exists(gameDataDir))
            dataFileCount = (uint)Directory.GetFiles(gameDataDir, "data.*").Length;
        Shmem.Write(gameDataDir, gameDataPath, bucketVersions, dataFileCount);

        // Data/{product}/shmem (product-specific directory)
        var productDir = Path.Combine(_data_dir, _product);
        var productPath = NormalizePath(productDir);
        Shmem.Write(productDir, productPath, bucketVersions, 0);
    }

    void WriteEcacheDirectories()
    {
        if (_data_dir == null) return;

        // ecache - encoding cache container (IDX files + shmem, no data file initially)
        var ecacheDir = Path.Combine(_data_dir, "ecache");
        WriteEmptyCascContainer(ecacheDir);
    }

    void WriteExtractDirectory()
    {
        if (_data_dir == null) return;

        // .extract directory is only created for bootstrapper (BTS) products
        // whose data dir is inside .battle.net/
        if (!_data_dir.Contains(".battle.net")) return;

        var extractDir = Path.Combine(_data_dir, ".extract");
        if (!Directory.Exists(extractDir))
            Directory.CreateDirectory(extractDir);
    }

    void WriteResidencyFiles()
    {
        if (_data_dir == null) return;

        // Product-specific .residency (Data/{product}/.residency)
        var productDir = Path.Combine(_data_dir, _product);
        if (Directory.Exists(productDir))
            File.Create(Path.Combine(productDir, ".residency")).Dispose();

        // BTS .residency (.battle.net/bts/.residency) - only for shared container installs
        if (_game_dir != null)
        {
            var btsDir = Path.Combine(_game_dir, ".battle.net", "bts");
            if (Directory.Exists(btsDir))
                File.Create(Path.Combine(btsDir, ".residency")).Dispose();
        }
    }

    void CleanupRepairMarker()
    {
        if (_gameDataDir == null) return;
        var repairMarker = Path.Combine(_gameDataDir, "RepairMarker.psv");
        if (File.Exists(repairMarker))
            File.Delete(repairMarker);
    }

    void WriteEmptyCascContainer(string dir, bool includeDataFile = false)
    {
        if (!Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        // Write 16 empty v7 IDX files (one per bucket, version 1)
        for (var i = 0; i < 16; i++)
        {
            var bucket = (byte)i;
            var path = Path.Combine(dir, $"{bucket:x2}00000001.idx");
            var idx = new IDX(path, bucket);
            idx.Write();
        }

        // Write shmem
        var normalizedPath = NormalizePath(dir);
        var bucketVersions = new uint[16];
        Array.Fill(bucketVersions, 1u);
        Shmem.Write(dir, normalizedPath, bucketVersions, 0);

        // Write empty data.000 (just segment headers) if requested
        if (includeDataFile)
        {
            var dataPath = Path.Combine(dir, "data.000");
            if (!File.Exists(dataPath))
            {
                var data = new Data(0, out _, NormalizePath(Path.GetDirectoryName(dir)!));
                data.Finalize(dir);
            }
        }
    }

    static async Task SaveConfigToInstall(Hash key, string? overrideFile, string dataDir)
    {
        if (key.IsEmpty()) return;
        var saveDir = Path.Combine(dataDir, "config", key.KeyString![0..2], key.KeyString[2..4]);
        var savePath = Path.Combine(saveDir, key.KeyString);
        if (File.Exists(savePath)) return;
        if (!Directory.Exists(saveDir)) Directory.CreateDirectory(saveDir);
        if (overrideFile != null)
            File.Copy(overrideFile, savePath);
        else
        {
            // Config was already loaded from CDN; re-read from cache if available
            var cachePath = Path.Combine("cache", "configs", key.KeyString);
            if (File.Exists(cachePath))
                File.Copy(cachePath, savePath);
        }
    }

    string _baseDir = "";

    /// <summary>
    /// Normalizes a path to match Agent's TACT path format:
    /// forward slashes, lowercase drive letter, no trailing separator.
    /// </summary>
    static string NormalizePath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var normalized = fullPath.Replace('\\', '/');
        // Lowercase drive letter (e.g., "C:/" -> "c:/")
        if (normalized.Length >= 2 && normalized[1] == ':')
            normalized = char.ToLower(normalized[0]) + normalized[1..];
        return normalized.TrimEnd('/');
    }

    async Task DownloadAndWriteFile(Hash eKey, CDN? cdn, CDNConfig? cdnConfig, ConcurrentDictionary<Hash, ArchiveIndex.IndexEntry>? archiveGroup, ulong size, bool truncateKey = false)
    {
        var writeSize = size + 30;
        var bucket = Data.cascGetBucketIndex(eKey);

        if (_gameDataDir == null)
            throw new NullReferenceException("_gameDataDir");

        if (idxMap == null)
            throw new NullReferenceException("idxMap");

        if (Working_Data == null)
        {
            Working_Data = new Data(0, out var segmentHeaderKeys, _baseDir);
            segmentHeaderKeyList = [ segmentHeaderKeys ];
        }

        if (idxMap.TryGetValue(bucket, out var idx))
        {
            if (!Working_Data.CanWrite((int)writeSize))
            {
                Working_Data.Finalize(_gameDataDir);
                var currentID = Working_Data.ID;
                Working_Data = new Data(currentID + 1, out var segmentHeaderKeys, _baseDir);
                segmentHeaderKeyList.Add(segmentHeaderKeys);
                GC.Collect();
            }

            var idxEntry = idx.Add(eKey, Working_Data.ID, (int)Working_Data.Offset, writeSize);
            var actualSize = await Working_Data.WriteDataEntry(idxEntry, cdn, cdnConfig, archiveGroup, truncateKey);

            // Update IDX entry with actual size if it differs from expected
            if (actualSize > 0 && actualSize != writeSize)
                idxEntry.Size = actualSize;
        }
    }

    void WriteProductDB(string? gameDir, Version? version, ProductConfig? productConfig, BuildConfig? buildConfig)
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
                Languages = { new LanguageSetting { Language = "enUS", Option = ProtoDatabase.LanguageOption.LangoptionTextAndSpeech } },
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
                    CompletedBuildKeys = { version?.BuildConfigHash.KeyString?.ToLower() ?? "" },
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
            PersistentJsonStorage = "{\"user_install_package_settings\":[]}"
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
        // Use original CDN info (not local override) for .build.info
        var cdnLocal = _cdn as CDNLocal;
        var build = new BuildInfo.Build
        {
            Branch = _cdn?.Name,
            Active = 1,
            BuildKey = _version?.BuildConfigHash ?? new Hash(),
            CDNKey = _version?.CdnConfigHash ?? new Hash(),
            InstallKey = new Hash(), // Agent leaves this empty
            IMSize = -1,
            CDNPath = _cdn?.Path,
            CDNHosts = cdnLocal?.OriginalHosts ?? _cdn?.Hosts,
            CDNServers = cdnLocal?.OriginalServers ?? _cdn?.Servers,
            Tags = BuildDynamicTags(),
            Armadillo = _productConfig?.all?.config?.decryption_key_name ?? "",
            LastActivated = "",
            Version = _version?.VersionsName,
            KeyRing = _version?.KeyRing ?? new Hash(),
            Product = _version?.Product
        };

        _buildInfo.AddBuild(build);
        _buildInfo.Write(path);
    }

    private string[] BuildDynamicTags()
    {
        // Build base tags from install tags + locale
        var baseTags = new List<string>();

        if (_installTags != null)
        {
            foreach (var tag in _installTags)
                baseTags.Add(tag);
        }

        baseTags.Add("enUS");

        // Format: "<baseTags> speech:<baseTags> text"
        // This is stored as a string[] that gets joined with spaces in BuildInfo.Write
        var result = new List<string>(baseTags);
        result.Add($"speech:{string.Join(" ", baseTags)}");
        result.Add("text");
        return result.ToArray();
    }

    private void WriteFlavorInfo(string? shared_game_dir, Version? version)
    {
        if (shared_game_dir == null)
            return;

        var filePath = Path.Combine(shared_game_dir, ".flavor.info");
        using var sw = new StreamWriter(filePath) { NewLine = "\n" };
        sw.WriteLine("Product Flavor!STRING:0");
        sw.WriteLine(version?.Product);
    }
}
