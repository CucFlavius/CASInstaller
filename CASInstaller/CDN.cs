using System.Text;

namespace CASInstaller;

public struct CDN
{
    public string? Product { get; set; }
    public string Name { get; set; }
    public string Path { get; set; }
    public string[] Hosts { get; set; }
    public string[] Servers { get; set; }
    public string ConfigPath { get; set; }
  

    public CDN(string data, string? product)
    {
        Product = product;
        var parts = data.Split('|');
        
        Name = parts[0];
        Path = parts[1];
        Hosts = parts[2].Split(' ');
        Servers = parts[3].Split(' ');
        ConfigPath = parts[4];
    }

    public static async Task<CDN[]> GetAllCDN(string? product)
    {
        var url = $@"http://us.patch.battle.net:1119/{product}/cdns";
        var data = await Utils.GetDataFromURL(url);
        
        var stringData = System.Text.Encoding.UTF8.GetString(data);
        var lines = stringData.Split('\n');
        var cdns = new List<CDN>();
        for (var index = 1; index < lines.Length; index++)  // Ignoring first line that has to table headers
        {
            var line = lines[index];
            
            // Skip comment lines that start with #
            if (line.StartsWith($"#") || string.IsNullOrWhiteSpace(line))
            {
                continue;
            }
            
            var cdn = new CDN(line, product);
            cdns.Add(cdn);
        }
        
        return cdns.ToArray();
    }
    
    public static async Task<CDN> GetCDN(string? product, string region = "us")
    {
        var url = $@"http://us.patch.battle.net:1119/{product}/cdns";
        var data = await Utils.GetDataFromURL(url);
        
        var stringData = System.Text.Encoding.UTF8.GetString(data);
        var lines = stringData.Split('\n');
        for (var index = 1; index < lines.Length; index++)  // Ignoring first line that has to table headers
        {
            var line = lines[index];
            
            // Skip comment lines that start with #
            if (line.StartsWith($"#"))
            {
                continue;
            }
            
            var cdn = new CDN(line, product);

            if (cdn.Name == region)
            {
                return cdn;
            }
        }
        
        throw new Exception("CDN not found");
    }
    
    public override string ToString()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"[yellow]Product:[/] {Product}");
        sb.AppendLine($"[yellow]Name:[/] {Name}");
        sb.AppendLine($"[yellow]Path:[/] {Path}");
        sb.AppendLine($"[yellow]Hosts:[/]");
        foreach (var host in Hosts)
        {
            sb.AppendLine($"  {host}");
        }
        sb.AppendLine($"[yellow]Servers:[/]");
        foreach (var server in Servers)
        {
            sb.AppendLine($"  {server}");
        }
        sb.AppendLine($"[yellow]ConfigPath:[/] {ConfigPath}");
        return sb.ToString();
    }
}