namespace CASInstaller;

internal abstract class Program
{
    private static async Task Main(string[] args)
    {
        const string _product = "wow_classic_era";
        const string _branch = "eu";
        const string _installPath = "";
    
        var game = new Game(_product, _branch);
        await game.Install(_installPath);
    }
}