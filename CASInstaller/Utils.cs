using System.Net.Http.Headers;
using Spectre.Console;

namespace CASInstaller;

public static class Utils
{
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