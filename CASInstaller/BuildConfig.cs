using System.Text;
using Spectre.Console;

namespace CASInstaller;

public struct BuildConfig
{
    public Hash Root;
    public Hash[] Download;
    public Hash[] Install;
    public Hash[] Encoding;
    public string[] EncodingSize;
    public Hash[] Size;
    public string[] SizeSize;
    public string? BuildName;
    public string? BuildPlaybuildInstaller;
    public string? BuildProduct;
    public string? BuildUid;
    public string? Patch;
    public string? PatchSize;
    public string? PatchConfig;
    public string? BuildBranch;
    public string? BuildNumber;
    public string? BuildAttributes;
    public string? BuildComments;
    public string? BuildCreator;
    public string? BuildFixedHash;
    public string? BuildReplayHash;
    public string? BuildManifestVersion;
    public string[] InstallSize;
    public string[] DownloadSize;
    public string? PartialPriority;
    public string? PartialPrioritySize;
    public string? BuildSignatureFile;
    public string[] PatchIndex;
    public string[] PatchIndexSize;

    public BuildConfig(byte[] data)
    {
        string content = System.Text.Encoding.UTF8.GetString(data);

        if (string.IsNullOrEmpty(content) || !content.StartsWith("# Build"))
        {
            Console.WriteLine("Error reading build config");
            return;
        }

        var lines = content.Split(["\n"], StringSplitOptions.RemoveEmptyEntries);

        foreach (var t in lines)
        {
            if (t.StartsWith($"#") || t.Length == 0)
                continue;

            string?[] cols = t.Split([" = "], StringSplitOptions.RemoveEmptyEntries);
            switch (cols[0])
            {
                case "root":
                    Root = new Hash(cols[1]);
                    break;
                case "download":
                    string?[] downloadEntries = cols[1].Split(' ');
                    Download = new Hash[downloadEntries.Length];
                    for (var i = 0; i < downloadEntries.Length; i++)
                    {
                        Download[i] = new Hash(downloadEntries[i]);
                    }
                    break;
                case "install":
                    string?[] installEntries = cols[1].Split(' ');
                    Install = new Hash[installEntries.Length];
                    for (var i = 0; i < installEntries.Length; i++)
                    {
                        Install[i] = new Hash(installEntries[i]);
                    }
                    break;
                case "encoding":
                    string?[] encodingEntries = cols[1].Split(' ');
                    Encoding = new Hash[encodingEntries.Length];
                    for (var i = 0; i < encodingEntries.Length; i++)
                    {
                        Encoding[i] = new Hash(encodingEntries[i]);
                    }
                    break;
                case "encoding-size":
                    EncodingSize = cols[1].Split(' ');
                    break;
                case "size":
                    string?[] sizeEntries = cols[1].Split(' ');
                    Size = new Hash[sizeEntries.Length];
                    for (var i = 0; i < sizeEntries.Length; i++)
                    {
                        Size[i] = new Hash(sizeEntries[i]);
                    }
                    break;
                case "size-size":
                    SizeSize = cols[1].Split(' ');
                    break;
                case "build-name":
                    BuildName = cols[1];
                    break;
                case "build-playbuild-installer":
                    BuildPlaybuildInstaller = cols[1];
                    break;
                case "build-product":
                    BuildProduct = cols[1];
                    break;
                case "build-uid":
                    BuildUid = cols[1];
                    break;
                case "patch":
                    Patch = cols[1];
                    break;
                case "patch-size":
                    PatchSize = cols[1];
                    break;
                case "patch-config":
                    PatchConfig = cols[1];
                    break;
                case "build-branch": // Overwatch
                    BuildBranch = cols[1];
                    break;
                case "build-num": // Agent
                case "build-number": // Overwatch
                case "build-version": // Catalog
                    BuildNumber = cols[1];
                    break;
                case "build-attributes": // Agent
                    BuildAttributes = cols[1];
                    break;
                case "build-comments": // D3
                    BuildComments = cols[1];
                    break;
                case "build-creator": // D3
                    BuildCreator = cols[1];
                    break;
                case "build-fixed-hash": // S2
                    BuildFixedHash = cols[1];
                    break;
                case "build-replay-hash": // S2
                    BuildReplayHash = cols[1];
                    break;
                case "build-t1-manifest-version":
                    BuildManifestVersion = cols[1];
                    break;
                case "install-size":
                    InstallSize = cols[1].Split(' ');
                    break;
                case "download-size":
                    DownloadSize = cols[1].Split(' ');
                    break;
                case "build-partial-priority":
                case "partial-priority":
                    PartialPriority = cols[1];
                    break;
                case "partial-priority-size":
                    PartialPrioritySize = cols[1];
                    break;
                case "build-signature-file":
                    BuildSignatureFile = cols[1];
                    break;
                case "patch-index":
                    PatchIndex = cols[1].Split(' ');
                    break;
                case "patch-index-size":
                    PatchIndexSize = cols[1].Split(' ');
                    break;
                default:
                    //Console.WriteLine("Unknown build config key: " + cols[0]);
                    break;
            }
        }
    }
    
    public static async Task<BuildConfig> GetBuildConfig(CDN? cdn, Hash? key, string? data_dir)
    {
        if (cdn == null || key == null) return new BuildConfig();
        
        var saveDir = Path.Combine(data_dir ?? "", "config", key?.KeyString?[0..2] ?? "", key?.KeyString?[2..4] ?? "");
        var savePath = Path.Combine(saveDir, key?.KeyString ?? "");
        
        if (File.Exists(savePath))
        {
            return new BuildConfig(await File.ReadAllBytesAsync(savePath));
        }
        else
        {
            var hosts = cdn?.Hosts;
            if (hosts == null) return new BuildConfig();
            
            foreach (var cdnURL in hosts)
            {
                var url = $@"http://{cdnURL}/{cdn?.Path}/config/{key?.UrlString}";
                var encryptedData = await Utils.GetDataFromURL(url);
                if (encryptedData == null) continue;
                byte[] data;
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

                return new BuildConfig(data);
            }
        }

        return new BuildConfig();
    }

    public override string ToString()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"[yellow]Root:[/] {Root}");
        if (Download != null)
            sb.AppendLine($"[yellow]Download:[/] {string.Join(" ", Download)}");
        if (Install != null)
            sb.AppendLine($"[yellow]Install:[/] {string.Join(" ", Install)}");
        if (Encoding != null)
            sb.AppendLine($"[yellow]Encoding:[/] {string.Join(" ", Encoding)}");
        if (EncodingSize != null)
            sb.AppendLine($"[yellow]EncodingSize:[/] {string.Join(" ", EncodingSize)}");
        if (Size != null)
            sb.AppendLine($"[yellow]Size:[/] {string.Join(" ", Size)}");
        if (SizeSize != null)
            sb.AppendLine($"[yellow]SizeSize:[/] {string.Join(" ", SizeSize)}");
        sb.AppendLine($"[yellow]BuildName:[/] {BuildName}");
        sb.AppendLine($"[yellow]BuildPlaybuildInstaller:[/] {BuildPlaybuildInstaller}");
        sb.AppendLine($"[yellow]BuildProduct:[/] {BuildProduct}");
        sb.AppendLine($"[yellow]BuildUid:[/] {BuildUid}");
        sb.AppendLine($"[yellow]Patch:[/] {Patch}");
        sb.AppendLine($"[yellow]PatchSize:[/] {PatchSize}");
        sb.AppendLine($"[yellow]PatchConfig:[/] {PatchConfig}");
        sb.AppendLine($"[yellow]BuildBranch:[/] {BuildBranch}");
        sb.AppendLine($"[yellow]BuildNumber:[/] {BuildNumber}");
        sb.AppendLine($"[yellow]BuildAttributes:[/] {BuildAttributes}");
        sb.AppendLine($"[yellow]BuildComments:[/] {BuildComments}");
        sb.AppendLine($"[yellow]BuildCreator:[/] {BuildCreator}");
        sb.AppendLine($"[yellow]BuildFixedHash:[/] {BuildFixedHash}");
        sb.AppendLine($"[yellow]BuildReplayHash:[/] {BuildReplayHash}");
        sb.AppendLine($"[yellow]BuildManifestVersion:[/] {BuildManifestVersion}");
        if (InstallSize != null)
            sb.AppendLine($"[yellow]InstallSize:[/] {string.Join(" ", InstallSize)}");
        if (DownloadSize != null)
            sb.AppendLine($"[yellow]DownloadSize:[/] {string.Join(" ", DownloadSize)}");
        sb.AppendLine($"[yellow]PartialPriority:[/] {PartialPriority}");
        sb.AppendLine($"[yellow]PartialPrioritySize:[/] {PartialPrioritySize}");
        sb.AppendLine($"[yellow]BuildSignatureFile:[/] {BuildSignatureFile}");
        if (PatchIndex != null)
            sb.AppendLine($"[yellow]PatchIndex:[/] {string.Join(" ", PatchIndex)}");
        if (PatchIndexSize != null)
            sb.AppendLine($"[yellow]PatchIndexSize:[/] {string.Join(" ", PatchIndexSize)}");
        return sb.ToString();
    }

    public void LogInfo()
    {
        AnsiConsole.MarkupLine("[bold blue]-------------------[/]");
        AnsiConsole.MarkupLine("[bold blue]--- Build Config --[/]");
        AnsiConsole.MarkupLine("[bold blue]-------------------[/]");
        AnsiConsole.Markup(this.ToString());
    }
}