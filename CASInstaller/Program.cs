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
        var (shared_game_dir, data_dir, installTags) = await ProcessProductConfig(cdn, version);
        var buildConfig = await ProcessBuildConfig(cdn, version, data_dir);
        await ProcessCDNConfig(cdn, version, data_dir);
        var encoding = await ProcessEncoding(buildConfig, cdn);
        await ProcessDownload(buildConfig, cdn);
        //await ProcessInstall(buildConfig, cdn, installTags, encoding, shared_game_dir);
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
    
    private static async Task<(string shared_game_dir, string data_dir, List<string> installTags)> ProcessProductConfig(CDN cdn, Version version)
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
    
    private static async Task ProcessCDNConfig(CDN cdn, Version version, string data_dir)
    {
        var cdnConfig = await CDNConfig.GetConfig(cdn, version.CdnConfigHash.KeyString, data_dir);
        
        AnsiConsole.MarkupLine("[bold blue]-------------------[/]");
        AnsiConsole.MarkupLine("[bold blue]--- CDN Config ----[/]");
        AnsiConsole.MarkupLine("[bold blue]-------------------[/]");
        AnsiConsole.Markup(cdnConfig.ToString());
        
        //ArchiveGroup: 7bf413cc27b5cfb65dbd591d1ea75b25
        //PatchArchiveGroup: b51a588a0fa557f7f490c2b103bfab3f
        //FileIndex: 708cb6c2f4083d1593f72d7154c3de62
        //PatchFileIndex: 6808653c55dc594fcc0a893679ae766f
        
        //var fileIndex = ArchiveIndex.GetDataIndex(cdn, cdnConfig.FileIndex, "data", data_dir).Result;
        //var patchFileIndex = ArchiveIndex.GetDataIndex(cdn, cdnConfig.PatchFileIndex, "patch", data_dir).Result;
        
        AnsiConsole.Progress().Start(ctx =>
        {
            if (cdnConfig.Archives != null)
            {
                var task1 = ctx.AddTask($"[green]Processing {cdnConfig.Archives.Length} archives[/]");
                task1.MaxValue = cdnConfig.Archives.Length;
                Parallel.ForEach(cdnConfig.Archives, archive =>
                {
                    task1.Increment(1);
                    var index = ArchiveIndex.GetDataIndex(cdn, archive, "data", data_dir).Result;
                });
            }

            if (cdnConfig.PatchArchives != null)
            {
                var task2 = ctx.AddTask($"[green]Processing {cdnConfig.PatchArchives.Length} patch archives[/]");
                task2.MaxValue = cdnConfig.PatchArchives.Length;
                Parallel.ForEach(cdnConfig.PatchArchives, archive =>
                {
                    task2.Increment(1);
                    var index = ArchiveIndex.GetDataIndex(cdn, archive, "patch", data_dir).Result;
                });
            }
        });
        
        //AnsiConsole.WriteLine( cdnConfig.Archives[307].KeyString);

        // Build archive-group
        // cdnConfig.ArchiveGroup
        
    }
    
    private static async Task<Encoding> ProcessEncoding(BuildConfig buildConfig, CDN cdn)
    {
        var encodingContentHash = buildConfig.Encoding[0];
        var encodingEncodedHash = buildConfig.Encoding[1];
        var encoding = await Encoding.GetEncoding(cdn, encodingEncodedHash);
        
        AnsiConsole.MarkupLine("[bold blue]-------------------[/]");
        AnsiConsole.MarkupLine("[bold blue]----- Encoding ----[/]");
        AnsiConsole.MarkupLine("[bold blue]-------------------[/]");
        AnsiConsole.Markup(encoding.ToString());
        return encoding;
    }
    
    private static async Task ProcessInstall(BuildConfig buildConfig, CDN cdn, List<string> installTags, Encoding encoding, string shared_game_dir)
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
        var filteredEntries = new List<InstallManifest.InstallFileEntry>();
        foreach (var entry in install.entries)
        {
            var checksOut = true;
            foreach (var tag in installTags)
            {
                if (!entry.tagStrings.Contains(tag))
                {
                    checksOut = false;
                }
            }
            
            if (checksOut)
                filteredEntries.Add(entry);
        }
        
        Parallel.ForEach(filteredEntries, installEntry =>
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
    }

    private static async Task ProcessDownload(BuildConfig buildConfig, CDN cdn)
    {
        var downloadContentHash = buildConfig.Download[0];
        var downloadEncodedHash = buildConfig.Download[1];
        var download = await DownloadManifest.GetDownload(cdn, downloadEncodedHash);
        
        AnsiConsole.MarkupLine("[bold blue]-------------------[/]");
        AnsiConsole.MarkupLine("[bold blue]----- Download -----[/]");
        AnsiConsole.MarkupLine("[bold blue]-------------------[/]");
        AnsiConsole.Markup(download.ToString());

        using var sw = new StreamWriter("download.txt");
        foreach (var entry in download.entries)
        {
            sw.WriteLine(entry.eKey.KeyString);
        }
    }
    
}