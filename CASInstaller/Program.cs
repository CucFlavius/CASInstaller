namespace CASInstaller;

internal abstract class Program
{
    private static async Task ListTags(string product, string branch,
        string? overrideHosts, string? localCdnPath,
        string? overrideCdnConfig, string? overrideCdnConfigFile,
        string? overrideBuildConfig, string? overrideBuildConfigFile)
    {
        BLTE.BLTEStream.ThrowOnMissingDecryptionKey = false;
        KeyService.LoadKeys();

        CDN? cdn;
        if (string.IsNullOrEmpty(localCdnPath))
            cdn = await CDNOnline.GetCDN(product, branch);
        else
            cdn = new CDNLocal(product, localCdnPath);

        if (cdn == null) { Console.WriteLine("Failed to get CDN info."); return; }
        if (overrideHosts != null) cdn.Hosts = overrideHosts.Split(' ');

        var version = await Version.GetVersion(product, branch);
        if (version == null) { Console.WriteLine("Failed to get version info."); return; }

        if (overrideCdnConfig != null) version.CdnConfigHash = new Hash(overrideCdnConfig);
        else if (overrideCdnConfigFile != null) version.CdnConfigHash = new Hash(Path.GetFileName(overrideCdnConfigFile));
        if (overrideBuildConfig != null) version.BuildConfigHash = new Hash(overrideBuildConfig);
        else if (overrideBuildConfigFile != null) version.BuildConfigHash = new Hash(Path.GetFileName(overrideBuildConfigFile));

        var productConfig = await ProductConfig.GetProductConfig(cdn, version.ProductConfigHash);
        if (!string.IsNullOrEmpty(productConfig?.all?.config?.decryption_key_name))
            ArmadilloCrypt.Init(productConfig.all.config.decryption_key_name);

        BuildConfig buildConfig;
        if (overrideBuildConfigFile != null)
            buildConfig = new BuildConfig(await File.ReadAllBytesAsync(overrideBuildConfigFile));
        else
            buildConfig = await BuildConfig.GetBuildConfig(cdn, version.BuildConfigHash, null);

        Console.WriteLine($"Product: {product}  Branch: {branch}  Version: {version.VersionsName}");
        Console.WriteLine();

        // Download manifest tags
        if (buildConfig.Download != null)
        {
            var download = await DownloadManifest.GetDownload(cdn, buildConfig.Download[^1]);
            if (download != null)
            {
                Console.WriteLine($"Download Manifest Tags ({download.tags.Length}):");
                foreach (var tag in download.tags)
                {
                    var count = 0;
                    for (var j = 0; j < tag.bitmap.Length; j++)
                        if (tag.bitmap[j]) count++;
                    Console.WriteLine($"  {tag.name,-30} type=0x{tag.type:X4}  entries={count}");
                }
                Console.WriteLine();
            }
        }

        // Install manifest tags
        if (buildConfig.Install != null)
        {
            var install = await InstallManifest.GetInstall(cdn, buildConfig.Install[^1]);
            if (install != null)
            {
                Console.WriteLine($"Install Manifest Tags ({install.tags.Length}):");
                foreach (var tag in install.tags)
                {
                    var count = 0;
                    for (var j = 0; j < tag.bitmap.Length; j++)
                        if (tag.bitmap[j]) count++;
                    Console.WriteLine($"  {tag.name,-30} type=0x{tag.type:X4}  entries={count}");
                }
            }
        }
    }

    private static async Task Main(string[] args)
    {
        string? _product = null;
        string? _branch = null;
        string? _installPath = null;
        string? _overrideCdnConfig = null;
        string? _overrideCdnConfigFile = null;
        string? _overrideBuildConfig = null;
        string? _overrideBuildConfigFile = null;
        string? _overrideHosts = null;
        string? _localCdnPath = null;
        bool _listTags = false;

        if (args.Length == 0)
        {
            Console.WriteLine("Usage: CASInstaller.exe" +
                              " -p|--product <product:string>" +
                              " [-b|--branch <branch:string>]" +
                              " [-i|--install-path <install-path:string>]" +
                              " [--override-cdn-config <cdn-config:16byteHexString|filePath>]" +
                              " [--override-build-config <build-config:16byteHexString|filePath>]" +
                              " [--override-hosts <hosts:stringSpaceSeparated>]");
            return;
        }

        // Parse args
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "-p":
                case "--product":
                    _product = args[++i];
                    break;
                case "-b":
                case "--branch":
                    _branch = args[++i];
                    break;
                case "-i":
                case "--install-path":
                    _installPath = args[++i];
                    break;
                case "--override-cdn-config":
                    var cdnConfigValue = args[++i];
                    if (File.Exists(cdnConfigValue))
                        _overrideCdnConfigFile = cdnConfigValue;
                    else
                        _overrideCdnConfig = cdnConfigValue;
                    break;
                case "--override-build-config":
                    var buildConfigValue = args[++i];
                    if (File.Exists(buildConfigValue))
                        _overrideBuildConfigFile = buildConfigValue;
                    else
                        _overrideBuildConfig = buildConfigValue;
                    break;
                case "--override-hosts":
                    _overrideHosts = args[++i];
                    break;
                case "--local-cdn-path":
                    _localCdnPath = args[++i];
                    break;
                case "--list-tags":
                    _listTags = true;
                    break;
            }
        }

        if (_product == null)
        {
            Console.WriteLine("Product not specified.");
            return;
        }

        _installPath ??= "";
        _branch ??= "us";

        if (_listTags)
        {
            await ListTags(_product, _branch, _overrideHosts, _localCdnPath,
                _overrideBuildConfig, _overrideBuildConfigFile,
                _overrideCdnConfig, _overrideCdnConfigFile);
            return;
        }

        // Install the game
        var game_settings = new Product.InstallSettings()
        {
            CreateBuildInfo = true,
            CreatePatchResult = true,
            CreateProductDB = true,
            CreateLauncherDB = true,
            CreateFlavorInfo = true,
            OverrideCDNConfig = _overrideCdnConfig,
            OverrideCDNConfigFile = _overrideCdnConfigFile,
            OverrideBuildConfig = _overrideBuildConfig,
            OverrideBuildConfigFile = _overrideBuildConfigFile,
            OverrideHosts = _overrideHosts,
            LocalCDNPath = _localCdnPath,
        };
        var game = new Product(_product, _branch, game_settings);
        await game.Install(_installPath);

        // Install the bootstrapper (Launcher)
        var product_tag = game._productConfig?.all.config.launcher_install_info.product_tag ?? "wow";
        var bootstrapper_product = game._productConfig?.all.config.launcher_install_info.bootstrapper_product ?? "bts";
        var bootstrapper_branch = game._productConfig?.all.config.launcher_install_info.bootstrapper_branch ?? "launcher";
        var bootstrapper_game_dir = Path.Combine(_installPath, game._productConfig?.all.config.form.game_dir.dirname ?? "World of Warcraft");
        var bootstrapper_data_dir = Path.Combine(bootstrapper_game_dir, ".battle.net");
        var bootstrapper_settings = new Product.InstallSettings()
        {
            CreateBuildInfo = false,
            CreatePatchResult = false,
            CreateProductDB = false,
            CreateLauncherDB = false,
            CreateFlavorInfo = false
        };
        var launcher = new Product(bootstrapper_product, bootstrapper_branch, bootstrapper_settings)
        {
            _game_dir = bootstrapper_game_dir,
            _shared_game_dir = bootstrapper_game_dir,
            _data_dir = bootstrapper_data_dir,
            _installTags = [product_tag]
        };
        await launcher.Install(_installPath);
        // The launcher directory is usually hidden
        File.SetAttributes(bootstrapper_data_dir, File.GetAttributes(bootstrapper_data_dir) | FileAttributes.Hidden);
    }
}
