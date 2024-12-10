using System.Text;
using Spectre.Console;

namespace CASInstaller;

public struct CDN
{
    private string? Product { get; set; }
    public string? Name { get; set; }
    public string? Path { get; set; }
    public string[]? Hosts { get; set; }
    public string[]? Servers { get; set; }
    public string? ConfigPath { get; set; }


    private CDN(string data, string? product)
    {
        Product = product;
        string?[] parts = data.Split('|');
        
        if (parts.Length != 5)
        {
            return;
        }
        
        Name = parts[0];
        Path = parts[1];
        Hosts = parts[2]?.Split(' ');
        Servers = parts[3]?.Split(' ');
        ConfigPath = parts[4];
    }
    
    public static async Task<CDN?> GetCDN(string? product, string? branch = "us")
    {
        var url = $@"http://us.patch.battle.net:1119/{product}/cdns";
        var data = await Utils.GetDataFromURL(url);

        if (data == null) return null;
        var stringData = System.Text.Encoding.UTF8.GetString(data);
        var lines = stringData.Split('\n');
        List<CDN> cdns = [];
        for (var index = 1; index < lines.Length; index++)  // Ignoring first line that has to table headers
        {
            var line = lines[index];
            
            // Skip comment lines that start with #
            if (line.StartsWith($"#"))
            {
                continue;
            }
            
            var cdn = new CDN(line, product);
            cdns.Add(cdn);
            if (cdn.Name == branch)
            {
                return cdn;
            }
        }

        // In case there isn't a branch with the same name as the product (eg. "launcher" for "bts")
        if (cdns.Count > 0)
        {
            return cdns[0];
        }

        return null;
    }
    
    public override string ToString()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"[yellow]Product:[/] {Product}");
        sb.AppendLine($"[yellow]Name:[/] {Name}");
        sb.AppendLine($"[yellow]Path:[/] {Path}");
        sb.AppendLine($"[yellow]Hosts:[/]");
        if (Hosts != null)
            foreach (var host in Hosts)
            {
                sb.AppendLine($"  {host}");
            }

        sb.AppendLine($"[yellow]Servers:[/]");
        if (Servers != null)
            foreach (var server in Servers)
            {
                sb.AppendLine($"  {server}");
            }

        sb.AppendLine($"[yellow]ConfigPath:[/] {ConfigPath}");
        return sb.ToString();
    }

    public void LogInfo()
    {
        AnsiConsole.MarkupLine("[bold blue]---------------[/]");
        AnsiConsole.MarkupLine("[bold blue]----- CDN -----[/]");
        AnsiConsole.MarkupLine("[bold blue]---------------[/]");
        AnsiConsole.Markup(this.ToString());
    }
}