using System.Text;
using Spectre.Console;

namespace CASInstaller;

public class Version
{
    public string? Product { get; set; }
    public string? Region { get; set; }
    public Hash BuildConfigHash { get; set; }
    public Hash CdnConfigHash { get; set; }
    public Hash KeyRing { get; set; }
    public int BuildId { get; set; }
    public string? VersionsName { get; set; }
    public Hash ProductConfigHash { get; set; }

    public Version(string data, string? product)
    {
        Product = product;
        string?[] parts = data.Split('|');
        
        Region = parts[0];
        BuildConfigHash = new Hash(parts[1]);
        CdnConfigHash = new Hash(parts[2]);
        KeyRing = new Hash(parts[3]);
        BuildId = int.Parse(parts[4]);
        VersionsName = parts[5];
        ProductConfigHash = new Hash(parts[6]);
    }

    public static async Task<Version[]> GetAllVersions(string? product)
    {
        var url = $@"http://us.patch.battle.net:1119/{product}/versions";
        var data = await Utils.GetDataFromURL(url);
        
        if (data == null)
            throw new ArgumentNullException(nameof(data), "Downloaded data cannot be null.");

        var stringData = System.Text.Encoding.UTF8.GetString(data);
        var lines = stringData.Split('\n');
        var versions = new List<Version>();
        for (var index = 1; index < lines.Length; index++)  // Ignoring first line that has table headers
        {
            var line = lines[index];
            
            // Skip comment lines that start with #
            if (line.StartsWith($"#") || string.IsNullOrEmpty(line))
            {
                continue;
            }
            
            var version = new Version(line, product);
            versions.Add(version);
        }
        
        return versions.ToArray();
    }
    
    public static async Task<Version> GetVersion(string? product, string? branch = "us")
    {
        var url = $@"http://us.patch.battle.net:1119/{product}/versions";
        var data = await Utils.GetDataFromURL(url);
        
        if (data == null)
        {
            throw  new ArgumentNullException(nameof(data), "Downloaded data cannot be null.");
        }

        var stringData = System.Text.Encoding.UTF8.GetString(data);
        var lines = stringData.Split('\n');
        for (var index = 1; index < lines.Length; index++)  // Ignoring first line that has table headers
        {
            var line = lines[index];
            
            // Skip comment lines that start with #
            if (line.StartsWith($"#"))
            {
                continue;
            }
            
            var version = new Version(line, product);

            if (version.Region == branch)
            {
                return version;
            }
        }
        
        throw new Exception("Version not found");
    }
    
    public override string ToString()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"[yellow]Product:[/] {Product}");
        sb.AppendLine($"[yellow]Region:[/] {Region}");
        sb.AppendLine($"[yellow]BuildConfig:[/] {BuildConfigHash}");
        sb.AppendLine($"[yellow]CDNConfig:[/] {CdnConfigHash}");
        sb.AppendLine($"[yellow]KeyRing:[/] {KeyRing}");
        sb.AppendLine($"[yellow]BuildId:[/] {BuildId}");
        sb.AppendLine($"[yellow]VersionsName:[/] {VersionsName}");
        sb.AppendLine($"[yellow]ProductConfig:[/] {ProductConfigHash}");
        return sb.ToString();
    }

    public void LogInfo()
    {
        AnsiConsole.MarkupLine("[bold blue]---------------[/]");
        AnsiConsole.MarkupLine("[bold blue]--- Version ---[/]");
        AnsiConsole.MarkupLine("[bold blue]---------------[/]");
        AnsiConsole.Markup(this.ToString());
    }
}