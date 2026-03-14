using Spectre.Console;

namespace CASInstaller;

public class CDNLocal : CDN
{
    // Original CDN info for .build.info (not used for downloading)
    public string[]? OriginalHosts { get; private set; }
    public string[]? OriginalServers { get; private set; }

    public CDNLocal(string product, string path) : base(product)
    {
        Hosts = [path];
        Servers = [];

        // Determine the Path and ConfigPath from real cdn
        var realCdn = CDNOnline.GetCDN(product).Result;
        if (realCdn == null)
        {
            throw new Exception($"CDN {product} not found");
        }
        Path = realCdn.Path;
        ConfigPath = realCdn.ConfigPath;
        Name = realCdn.Name;
        OriginalHosts = realCdn.Hosts;
        OriginalServers = realCdn.Servers;
    }

    byte[]? GetDataFromPath(string url, int start, int size)
    {
        var path = System.IO.Path.Combine(Hosts[0], url);
        if (!File.Exists(path))
        {
            AnsiConsole.MarkupLine($"[yellow]Missing local CDN file:[/] {path}");
            return null;
        }

        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read);
        fs.Seek(start, SeekOrigin.Begin);
        using var br = new BinaryReader(fs);
        return br.ReadBytes(size);
    }

    byte[]? GetDataFromPath(string url)
    {
        var path = System.IO.Path.Combine(Hosts[0], url);
        if (!File.Exists(path))
        {
            AnsiConsole.MarkupLine($"[yellow]Missing local CDN file:[/] {path}");
            return null;
        }

        return File.ReadAllBytes(path);
    }

    public override Task<byte[]?> GetCDNConfig(Hash key)
    {
        // Try ConfigPath first, then fall back to Path/config (matching CDNOnline behavior)
        var configPaths = new[] { ConfigPath, $"{Path}/config" };

        foreach (var configPath in configPaths)
        {
            var fullPath = System.IO.Path.Combine(Hosts[0], $"{configPath}/{key.UrlString}");
            if (!File.Exists(fullPath)) continue;

            var encryptedData = GetDataFromPath($"{configPath}/{key.UrlString}");
            var data = ArmadilloCrypt.Instance == null ? encryptedData : ArmadilloCrypt.Instance?.DecryptData(key, encryptedData);
            return Task.FromResult(data);
        }

        AnsiConsole.MarkupLine($"[bold red]Failed to load config from local CDN:[/] {key}");
        return Task.FromResult<byte[]?>(null);
    }

    public override Task<byte[]?> GetConfig(Hash key)
    {
        // Try Path/config first, then fall back to ConfigPath (matching CDNOnline behavior)
        var configPaths = new[] { $"{Path}/config", ConfigPath };

        foreach (var configPath in configPaths)
        {
            var fullPath = System.IO.Path.Combine(Hosts[0], $"{configPath}/{key.UrlString}");
            if (!File.Exists(fullPath)) continue;

            var encryptedData = GetDataFromPath($"{configPath}/{key.UrlString}");
            var data = ArmadilloCrypt.Instance == null ? encryptedData : ArmadilloCrypt.Instance?.DecryptData(key, encryptedData);
            return Task.FromResult(data);
        }

        AnsiConsole.MarkupLine($"[bold red]Failed to load config from local CDN:[/] {key}");
        return Task.FromResult<byte[]?>(null);
    }

    public override Task<byte[]?> GetData(Hash key)
    {
        var encryptedData = GetDataFromPath($"{Path}/data/{key.UrlString}");
        if (encryptedData == null) return Task.FromResult<byte[]?>(null);
        var data = ArmadilloCrypt.Instance == null ? encryptedData : ArmadilloCrypt.Instance?.DecryptData(key, encryptedData);
        return Task.FromResult(data);
    }

    public override Task<byte[]?> GetData(Hash key, int start, int size)
    {
        var encryptedData = GetDataFromPath($"{Path}/data/{key.UrlString}", start, size);
        if (encryptedData == null) return Task.FromResult<byte[]?>(null);
        var data = ArmadilloCrypt.Instance == null ? encryptedData : ArmadilloCrypt.Instance?.DecryptData(key, encryptedData);
        return Task.FromResult(data);
    }

    public override Task<byte[]> GetPatch(Hash key)
    {
        var encryptedData = GetDataFromPath($"{Path}/patch/{key.UrlString}");
        if (encryptedData == null) return Task.FromResult<byte[]>(null);
        var data = ArmadilloCrypt.Instance == null ? encryptedData : ArmadilloCrypt.Instance?.DecryptData(key, encryptedData);
        return Task.FromResult(data);
    }

    public override Task<byte[]?> GetDataIndex(Hash key)
    {
        var encryptedData = GetDataFromPath($"{Path}/data/{key.UrlString}.index");
        if (encryptedData == null) return Task.FromResult<byte[]?>(null);
        var data = ArmadilloCrypt.Instance == null ? encryptedData : ArmadilloCrypt.Instance?.DecryptData(key, encryptedData);
        return Task.FromResult(data);
    }

    public override Task<byte[]?> GetPatchIndex(Hash key)
    {
        var encryptedData = GetDataFromPath($"{Path}/patch/{key.UrlString}.index");
        if (encryptedData == null) return Task.FromResult<byte[]?>(null);
        var data = ArmadilloCrypt.Instance == null ? encryptedData : ArmadilloCrypt.Instance?.DecryptData(key, encryptedData);
        return Task.FromResult(data);
    }
}
