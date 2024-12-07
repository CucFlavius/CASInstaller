using System.Text;
using Spectre.Console;

namespace CASInstaller;

public struct CDNConfig
{
    public readonly Hash[]? Archives;
    public readonly int[]? ArchivesIndexSize;
    public Hash ArchiveGroup;
    
    public readonly Hash[]? PatchArchives;
    public readonly int[]? PatchArchivesIndexSize;
    public Hash PatchArchiveGroup;

    public Hash FileIndex;
    public int FileIndexSize;

    public Hash PatchFileIndex;
    public int PatchFileIndexSize;
    
    public CDNConfig(string data)
    {
        if (string.IsNullOrEmpty(data))
            return;
        
        var lines = data.Split('\n');

        foreach (var line in lines)
        {
            if (string.IsNullOrEmpty(line))
                continue;
            
            if (line.StartsWith('#'))
                continue;
            
            var parts = line.Split(" = ");
            var key = parts[0];
            var value = parts[1];

            switch (key)
            {
                case "archives":
                    var archiveParts = value.Split(" ");
                    Archives = new Hash[archiveParts.Length];
                    for (var i = 0; i < archiveParts.Length; i++)
                    {
                        Archives[i] = new Hash(archiveParts[i]);
                    }
                    break;
                case "archives-index-size":
                    var archiveIndexParts = value.Split(" ");
                    ArchivesIndexSize = new int[archiveIndexParts.Length];
                    for (var i = 0; i < archiveIndexParts.Length; i++)
                    {
                        ArchivesIndexSize[i] = int.Parse(archiveIndexParts[i]);
                    }
                    break;
                case "archive-group":
                    ArchiveGroup = new Hash(value);
                    break;
                case "patch-archives":
                    var patchArchiveParts = value.Split(" ");
                    PatchArchives = new Hash[patchArchiveParts.Length];
                    for (var i = 0; i < patchArchiveParts.Length; i++)
                    {
                        PatchArchives[i] = new Hash(patchArchiveParts[i]);
                    }
                    break;
                case "patch-archives-index-size":
                    var patchArchiveIndexParts = value.Split(" ");
                    PatchArchivesIndexSize = new int[patchArchiveIndexParts.Length];
                    for (var i = 0; i < patchArchiveIndexParts.Length; i++)
                    {
                        PatchArchivesIndexSize[i] = int.Parse(patchArchiveIndexParts[i]);
                    }
                    break;
                case "patch-archive-group":
                    PatchArchiveGroup = new Hash(value);
                    break;
                case "file-index":
                    FileIndex = new Hash(value);
                    break;
                case "file-index-size":
                    FileIndexSize = int.Parse(value);
                    break;
                case "patch-file-index":
                    PatchFileIndex = new Hash(value);
                    break;
                case "patch-file-index-size":
                    PatchFileIndexSize = int.Parse(value);
                    break;
            }
        }
    }
    
    public static async Task<CDNConfig> GetConfig(CDN? cdn, Hash? key, string? data_dir)
    {
        if (cdn == null || key == null) return new CDNConfig(string.Empty);
        
        var saveDir = Path.Combine(data_dir ?? "", "config", key?.KeyString?[0..2] ?? "", key?.KeyString?[2..4] ?? "");
        var savePath = Path.Combine(saveDir, key?.KeyString ?? "");
        
        if (File.Exists(savePath))
        {
            var data = await File.ReadAllBytesAsync(savePath);
            var stringData = System.Text.Encoding.UTF8.GetString(data);
            return new CDNConfig(stringData);
        }
        else
        {
            var hosts = cdn?.Hosts;
            if (hosts == null) return new CDNConfig(string.Empty);
            
            foreach (var cdnURL in hosts)
            {
                var url = $@"http://{cdnURL}/{cdn?.Path}/config/{key?.UrlString}";
                var encryptedData = await Utils.GetDataFromURL(url);
                if (encryptedData == null) continue;
                byte[]? data;
                if (ArmadilloCrypt.Instance == null)
                    data = encryptedData;
                else
                    data = ArmadilloCrypt.Instance?.DecryptData(key, encryptedData);
                if (data == null) continue;

                if (data_dir != null)
                {
                    if (!Directory.Exists(saveDir))
                        Directory.CreateDirectory(saveDir);
                    await File.WriteAllBytesAsync(savePath, data);
                }

                var stringData = System.Text.Encoding.UTF8.GetString(data);
                return new CDNConfig(stringData);
            }
        }

        return new CDNConfig(string.Empty);
    }
    
    public override string ToString()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"[yellow]Archives:[/] {Archives?.Length}");
        sb.AppendLine($"[yellow]ArchivesIndexSize:[/] {ArchivesIndexSize?.Length}");
        sb.AppendLine($"[yellow]ArchiveGroup:[/] {ArchiveGroup}");
        sb.AppendLine($"[yellow]PatchArchives:[/] {PatchArchives?.Length}");
        sb.AppendLine($"[yellow]PatchArchivesIndexSize:[/] {PatchArchivesIndexSize?.Length}");
        sb.AppendLine($"[yellow]PatchArchiveGroup:[/] {PatchArchiveGroup}");
        sb.AppendLine($"[yellow]FileIndex:[/] {FileIndex}");
        sb.AppendLine($"[yellow]FileIndexSize:[/] {FileIndexSize}");
        sb.AppendLine($"[yellow]PatchFileIndex:[/] {PatchFileIndex}");
        sb.AppendLine($"[yellow]PatchFileIndexSize:[/] {PatchFileIndexSize}");
        return sb.ToString();
    }

    public void LogInfo()
    {
        AnsiConsole.MarkupLine("[bold blue]-------------------[/]");
        AnsiConsole.MarkupLine("[bold blue]--- CDN Config ----[/]");
        AnsiConsole.MarkupLine("[bold blue]-------------------[/]");
        AnsiConsole.Markup(this.ToString());
    }
}