using System.Net.Http.Headers;
using System.Text;
using Spectre.Console;

namespace CASInstaller;

public class CDN
{
    private HttpClient? _client;
    
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
    
    public void Initialize()
    {
        _client = new HttpClient();
        _client.Timeout = new TimeSpan(0, 1, 0);
    }
    
    public async Task<byte[]?> GetDataFromURL(string url, int start, int size)
    {
        if (url == null)
            throw new Exception("CDN.GetDataFromURL URL is null");

        _client ??= new HttpClient();
        
        byte[]? data = null;

        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Range = new RangeHeaderValue(start, start + size - 1);
            var response = await _client.SendAsync(request);
            data = await response.Content.ReadAsByteArrayAsync();
        }
        catch (HttpRequestException re)
        {
            AnsiConsole.MarkupLine($"[bold red]Download Failed:[/] {url}");
        }
        catch (Exception e)
        {
            AnsiConsole.MarkupLine($"[bold red]Download Failed:[/] {url}");
            AnsiConsole.WriteException(e);
            throw;
        }
        return data;
    }
    
    public async Task<byte[]?> GetDataFromURL(string url)
    {
        byte[]? data = null;
        
        _client ??= new HttpClient();
        
        try
        {
            data = await _client.GetByteArrayAsync(url);
        }
        catch (HttpRequestException re)
        {
            AnsiConsole.MarkupLine($"[bold red]Download Failed:[/] {url}");
        }
        catch (Exception e)
        {
            AnsiConsole.MarkupLine($"[bold red]Download Failed:[/] {url}");
            AnsiConsole.WriteException(e);
            throw;
        }
        
        return data;
    }

    public static async Task<List<CDN>> GetAllCDN(string? product)
    {
        var url = $@"http://us.patch.battle.net:1119/{product}/cdns";
        
        byte[]? data = null;
        var client = new HttpClient();
        try
        {
            data = await client.GetByteArrayAsync(url);
        }
        catch (HttpRequestException re)
        {
            AnsiConsole.MarkupLine($"[bold red]Download Failed:[/] {url}");
        }
        catch (Exception e)
        {
            AnsiConsole.MarkupLine($"[bold red]Download Failed:[/] {url}");
            AnsiConsole.WriteException(e);
            throw;
        }
        
        if (data == null) return [];
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
        }

        return cdns;
    }

    public static async Task<CDN?> GetCDN(string? product, string? branch = "us")
    {
        var cdns = await GetAllCDN(product);
        if (cdns == null) return null;
        foreach (var cdn in cdns)
        {
            if (cdn.Name == branch)
            {
                return cdn;
            }
        }
        
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