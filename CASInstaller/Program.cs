namespace CASInstaller;

internal abstract class Program
{
    private static async Task Main(string[] args)
    {
        string? _product = null;
        string? _branch = null;
        string? _installPath = null;
        string? _overrideCdnConfig = null;
        string? _overrideBuildConfig = null;
        string? _overrideHosts = null;

        if (args.Length == 0)
        {
            Console.WriteLine("Usage: CASInstaller.exe" +
                              " -p|--product <product:string>" +
                              " [-b|--branch <branch:string>]" +
                              " [-i|--install-path <install-path:string>]" +
                              " [--override-cdn-config <cdn-config:16byteHexString>]" +
                              " [--override-build-config <build-config:16byteHexString>]" +
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
                    _overrideCdnConfig = args[++i];
                    break;
                case "--override-build-config":
                    _overrideBuildConfig = args[++i];
                    break;
                case "--override-hosts":
                    _overrideHosts = args[++i];
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
        
        // Install the game
        var game_settings = new Product.InstallSettings()
        {
            CreateBuildInfo = true,
            CreatePatchResult = true,
            CreateProductDB = true,
            CreateLauncherDB = true,
            CreateFlavorInfo = true,
            OverrideCDNConfig = _overrideCdnConfig,
            OverrideBuildConfig = _overrideBuildConfig,
            OverrideHosts = _overrideHosts,
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