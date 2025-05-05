using Spectre.Console;

namespace CASInstaller;

public class CDNLocal : CDN
{
    string localPath;

    public CDNLocal(string? product, string? path) : base(product)
    {
        if (string.IsNullOrEmpty(path))
        {
            throw new ArgumentException("Path cannot be null or empty", nameof(path));
        }

        localPath = path;

        // Determine the Path and ConfigPath from real cdn
        var realCdn = CDNOnline.GetCDN(product).Result;
        if (realCdn == null)
        {
            throw new Exception($"CDN {product} not found");
        }
        Path = realCdn.Path;
        ConfigPath = realCdn.ConfigPath;
    }

    Task<byte[]> GetDataFromURL(string url, int start, int size)
    {
        if (url == null)
            throw new Exception("CDN.GetDataFromURL URL is null");

        try
        {
            var path = System.IO.Path.Combine(localPath, url);
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read);
            fs.Seek(start, SeekOrigin.Begin);
            using var br = new BinaryReader(fs);
            var data = br.ReadBytes(size);
            return Task.FromResult<byte[]>(data);
        }
        catch (Exception e)
        {
            AnsiConsole.WriteException(e);
            throw;
        }
    }

    Task<byte[]> GetDataFromURL(string url)
    {
        if (url == null)
            throw new Exception("CDN.GetDataFromURL URL is null");

        try
        {
            var path = System.IO.Path.Combine(localPath, url);
            return File.ReadAllBytesAsync(path);
        }
        catch (Exception e)
        {
            AnsiConsole.WriteException(e);
            throw;
        }
    }

    public override Task<byte[]?> GetCDNConfig(Hash key)
    {
        throw new NotImplementedException();
    }

    public override Task<byte[]?> GetConfig(Hash key)
    {
        throw new NotImplementedException();
    }

    public override Task<byte[]?> GetData(Hash key)
    {
        throw new NotImplementedException();
    }

    public override Task<byte[]?> GetData(Hash key, int start, int size)
    {
        throw new NotImplementedException();
    }

    public override Task<byte[]> GetPatch(Hash key)
    {
        throw new NotImplementedException();
    }
}
