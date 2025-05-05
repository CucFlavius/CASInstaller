using Spectre.Console;

namespace CASInstaller;

public class CDNLocal : CDN
{
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
    }

    byte[] GetDataFromPath(string url, int start, int size)
    {
        if (url == null)
            throw new Exception("CDN.GetDataFromURL URL is null");

        try
        {
            var path = System.IO.Path.Combine(Hosts[0], url);
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read);
            fs.Seek(start, SeekOrigin.Begin);
            using var br = new BinaryReader(fs);
            var data = br.ReadBytes(size);
            return data;
        }
        catch (Exception e)
        {
            AnsiConsole.WriteException(e);
            throw;
        }
    }

    byte[] GetDataFromPath(string url)
    {
        if (url == null)
            throw new Exception("CDN.GetDataFromURL URL is null");

        try
        {
            var path = System.IO.Path.Combine(Hosts[0], url);
            return File.ReadAllBytes(path);
        }
        catch (Exception e)
        {
            AnsiConsole.WriteException(e);
            throw;
        }
    }

    public override Task<byte[]?> GetCDNConfig(Hash key)
    {
        try
        {
            var encryptedData = GetDataFromPath($"{ConfigPath}/{key.UrlString}");
            var data = ArmadilloCrypt.Instance == null ? encryptedData : ArmadilloCrypt.Instance?.DecryptData(key, encryptedData);
            return Task.FromResult(data);
        }
        catch (Exception e)
        {
            AnsiConsole.WriteException(e);
            return Task.FromResult<byte[]>(null);
        }
    }

    public override Task<byte[]?> GetConfig(Hash key)
    {
        try
        {
            var encryptedData = GetDataFromPath($"{Path}/config/{key.UrlString}");
            var data = ArmadilloCrypt.Instance == null ? encryptedData : ArmadilloCrypt.Instance?.DecryptData(key, encryptedData);
            return Task.FromResult(data);
        }
        catch (Exception e)
        {
            AnsiConsole.WriteException(e);
            return Task.FromResult<byte[]>(null);
        }
    }

    public override Task<byte[]?> GetData(Hash key)
    {
        try
        {
            var encryptedData = GetDataFromPath($"{Path}/data/{key.UrlString}");
            var data = ArmadilloCrypt.Instance == null ? encryptedData : ArmadilloCrypt.Instance?.DecryptData(key, encryptedData);
            return Task.FromResult(data);
        }
        catch (Exception e)
        {
            AnsiConsole.WriteException(e);
            return Task.FromResult<byte[]>(null);
        }
    }

    public override Task<byte[]?> GetData(Hash key, int start, int size)
    {
        try
        {
            var encryptedData = GetDataFromPath($"{Path}/data/{key.UrlString}", start, size);
            var data = ArmadilloCrypt.Instance == null ? encryptedData : ArmadilloCrypt.Instance?.DecryptData(key, encryptedData);
            return Task.FromResult(data);
        }
        catch (Exception e)
        {
            AnsiConsole.WriteException(e);
            return Task.FromResult<byte[]>(null);
        }
    }

    public override Task<byte[]> GetPatch(Hash key)
    {
        try
        {
            var encryptedData = GetDataFromPath($"{Path}/patch/{key.UrlString}");
            var data = ArmadilloCrypt.Instance == null ? encryptedData : ArmadilloCrypt.Instance?.DecryptData(key, encryptedData);
            return Task.FromResult(data);
        }
        catch (Exception e)
        {
            AnsiConsole.WriteException(e);
            return Task.FromResult<byte[]>(null);
        }
    }
}
