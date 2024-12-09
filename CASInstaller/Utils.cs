using System.Net.Http.Headers;
using Spectre.Console;

namespace CASInstaller;

public static class Utils
{
    public static async Task<byte[]?> GetDataFromURL(string url)
    {
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
        return data;
    }
    
    public static async Task<byte[]?> GetDataFromURL(string url, int start, int size)
    {
        byte[]? data = null;
        var client = new HttpClient();
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Range = new RangeHeaderValue(start, start + size - 1);
            var response = await client.SendAsync(request);
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
    
    public static byte[]? GetFromCache(string path, string cache_path)
    {
        if (!Directory.Exists(cache_path))
        {
            Directory.CreateDirectory(cache_path);
        }
        
        return !File.Exists(path) ? null : File.ReadAllBytes(path);
    }

    public static void CacheData(string path, byte[]? data)
    {
        if (!Directory.Exists(Path.GetDirectoryName(path)))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? string.Empty);
        }
        
        if (data != null)
            File.WriteAllBytes(path, data);
    }
}