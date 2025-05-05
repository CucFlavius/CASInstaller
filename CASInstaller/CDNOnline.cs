using System.Net.Http.Headers;
using Spectre.Console;

namespace CASInstaller;

public class CDNOnline : CDN
{
    HttpClient? _client;

    CDNOnline(string? product, string? data) : base(product)
    {
        if (string.IsNullOrEmpty(data))
        {
            throw new ArgumentException("Data cannot be null or empty", nameof(data));
        }

        string?[] parts = data.Split('|');

        if (parts.Length != 5)
        {
            return;
        }

        Name = parts[0] ?? "";
        Path = parts[1] ?? "";
        Hosts = parts[2]?.Split(' ') ?? [];
        Servers = parts[3]?.Split(' ') ?? [];
        ConfigPath = parts[4] ?? "";

        _client = new HttpClient();
        _client.Timeout = new TimeSpan(0, 1, 0);
    }

    static async Task<List<CDN>> GetAllCDN(string? product)
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
            if (line.StartsWith($"#") || string.IsNullOrEmpty(line))
            {
                continue;
            }

            var cdn = new CDNOnline(product, line);
            cdns.Add(cdn);
        }

        return cdns;
    }

    public static async Task<CDN?> GetCDN(string? product, string? branch = "us")
    {
        var cdns = await GetAllCDN(product);
        foreach (var cdn in cdns)
        {
            if (cdn.Name == branch)
            {
                return cdn;
            }
        }

        return cdns.Count > 0 ? cdns[0] : null;
    }

    async Task<byte[]?> GetDataFromURL(string url, int start, int size)
    {
        if (url == null)
            throw new Exception("CDN.GetDataFromURL URL is null");

        _client ??= new HttpClient();

        byte[]? data = null;

        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Range = new RangeHeaderValue(start, start + size - 1);
            var response = await _client?.SendAsync(request)!;
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

    async Task<byte[]?> GetDataFromURL(string url)
    {
        _client ??= new HttpClient();

        try
        {
            return await _client.GetByteArrayAsync(url);
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

        return null;
    }

    public override async Task<byte[]?> GetData(Hash key)
    {
        if (key.IsEmpty())
            throw new Exception("CDN.GetConfig Key is empty");

        byte[]? data = null;
        foreach (var host in Hosts)
        {
            try
            {
                var encryptedData = await GetDataFromURL($"http://{host}/{Path}/data/{key.UrlString}");
                data = ArmadilloCrypt.Instance == null ? encryptedData : ArmadilloCrypt.Instance?.DecryptData(key, encryptedData);
            }
            catch (Exception e)
            {
                AnsiConsole.WriteException(e);
            }
        }

        return data;
    }

    public override async Task<byte[]> GetPatch(Hash key)
    {
        if (key.IsEmpty())
            throw new Exception("CDN.GetConfig Key is empty");

        byte[]? data = null;
        foreach (var host in Hosts)
        {
            try
            {
                var encryptedData = await GetDataFromURL($"http://{host}/{Path}/patch/{key.UrlString}");
                data = ArmadilloCrypt.Instance == null ? encryptedData : ArmadilloCrypt.Instance?.DecryptData(key, encryptedData);
            }
            catch (Exception e)
            {
                AnsiConsole.WriteException(e);
            }
        }

        return data;
    }

    public override async Task<byte[]?> GetData(Hash key, int start, int size)
    {
        if (key.IsEmpty())
            throw new Exception("CDN.GetConfig Key is empty");

        byte[]? data = null;
        foreach (var host in Hosts)
        {
            try
            {
                var encryptedData = await GetDataFromURL($"http://{host}/{Path}/{key.UrlString}", start, size);
                data = ArmadilloCrypt.Instance == null ? encryptedData : ArmadilloCrypt.Instance?.DecryptData(key, encryptedData);
            }
            catch (Exception e)
            {
                AnsiConsole.WriteException(e);
            }
        }

        return data;
    }

    public override async Task<byte[]?> GetConfig(Hash key)
    {
        if (key.IsEmpty())
            throw new Exception("CDN.GetConfig Key is empty");

        byte[]? data = null;
        foreach (var host in Hosts)
        {
            try
            {
                var encryptedData = await GetDataFromURL($"http://{host}/{Path}/config/{key.UrlString}");
                data = ArmadilloCrypt.Instance == null ? encryptedData : ArmadilloCrypt.Instance?.DecryptData(key, encryptedData);
            }
            catch (Exception e)
            {
                AnsiConsole.WriteException(e);
            }
        }

        return data;
    }

    public override async Task<byte[]?> GetCDNConfig(Hash key)
    {
        if (key.IsEmpty())
            throw new Exception("CDN.GetCDNConfig Key is empty");

        byte[]? data = null;
        foreach (var host in Hosts)
        {
            try
            {
                var encryptedData = await GetDataFromURL($"http://{host}/{ConfigPath}/{key.UrlString}");
                data = ArmadilloCrypt.Instance == null ? encryptedData : ArmadilloCrypt.Instance?.DecryptData(key, encryptedData);
            }
            catch (Exception e)
            {
                AnsiConsole.WriteException(e);
            }
        }

        return data;
    }
}
