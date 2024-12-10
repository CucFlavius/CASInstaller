namespace CASInstaller;

internal abstract class Program
{
    private static async Task Main(string[] args)
    {
        const string _product = "wow_classic_era";
        const string _branch = "eu";
        const string _installPath = "";
    
        // Install the game
        var game_settings = new Product.InstallSettings()
        {
            CreateBuildInfo = true,
            CreatePatchResult = true,
            CreateProductDB = true,
            CreateLauncherDB = true,
            CreateFlavorInfo = true
        };
        var game = new Product(_product, _branch, game_settings);
        await game.Install(_installPath);
        
        // Install the bootstrapper (Launcher)
        var product_tag = game._productConfig?.all.config.launcher_install_info.product_tag ?? "wow";
        var bootstrapper_product = game._productConfig?.all.config.launcher_install_info.bootstrapper_product ?? "bts";
        var bootstrapper_branch = game._productConfig?.all.config.launcher_install_info.bootstrapper_branch ?? "launcher";
        var bootstrapper_game_dir = Path.Combine(_installPath, game._productConfig?.all.config.form.game_dir.dirname ?? "World of Warcraft");
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
            _data_dir = Path.Combine(bootstrapper_game_dir, ".battle.net"),
            _installTags = [product_tag]
        };
        await launcher.Install(_installPath);
    }
}