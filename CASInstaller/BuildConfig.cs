using System.Text;
namespace CASInstaller;

public struct BuildConfig
{
    public string Root;
    public string?[] Download;
    public string?[] Install;
    public string[] Encoding;
    public string[] EncodingSize;
    public string[] Size;
    public string[] SizeSize;
    public string BuildName;
    public string BuildPlaybuildInstaller;
    public string BuildProduct;
    public string BuildUid;
    public string Patch;
    public string PatchSize;
    public string PatchConfig;
    public string BuildBranch;
    public string BuildNumber;
    public string BuildAttributes;
    public string BuildComments;
    public string BuildCreator;
    public string BuildFixedHash;
    public string BuildReplayHash;
    public string BuildManifestVersion;
    public string[] InstallSize;
    public string[] DownloadSize;
    public string PartialPriority;
    public string PartialPrioritySize;
    public string BuildSignatureFile;
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

            var cols = t.Split([" = "], StringSplitOptions.RemoveEmptyEntries);
            switch (cols[0])
            {
                case "root":
                    Root = cols[1];
                    break;
                case "download":
                    Download = cols[1].Split(' ');
                    break;
                case "install":
                    Install = cols[1].Split(' ');
                    break;
                case "encoding":
                    Encoding = cols[1].Split(' ');
                    break;
                case "encoding-size":
                    EncodingSize = cols[1].Split(' ');
                    break;
                case "size":
                    Size = cols[1].Split(' ');
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
    
    public static async Task<BuildConfig> GetBuildConfig(CDN cdn, string? key, string data_dir)
    {
        if (key == null) return new BuildConfig();
        
        var saveDir = Path.Combine(data_dir, "config", key[0..2], key[2..4]);
        var savePath = Path.Combine(saveDir, key);
        
        if (File.Exists(savePath))
        {
            return new BuildConfig(await File.ReadAllBytesAsync(savePath));
        }
        else
        {
            foreach (var cdnURL in cdn.Hosts)
            {
                var url = $@"http://{cdnURL}/{cdn.Path}/config/{key[0..2]}/{key[2..4]}/{key}";
                var encryptedData = await Utils.GetDataFromURL(url);
                if (encryptedData == null) continue;
                byte[] data;
                if (ArmadilloCrypt.Instance == null)
                    data = encryptedData;
                else
                    data = ArmadilloCrypt.Instance?.DecryptData(key, encryptedData);

                if (data == null) continue;

                if (!Directory.Exists(saveDir))
                    Directory.CreateDirectory(saveDir);
                await File.WriteAllBytesAsync(savePath, data);

                return new BuildConfig(data);
            }
        }

        return new BuildConfig();
    }

    public override string ToString()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"[yellow]Root:[/] {Root}");
        sb.AppendLine($"[yellow]Download:[/] {string.Join(" ", Download)}");
        sb.AppendLine($"[yellow]Install:[/] {string.Join(" ", Install)}");
        sb.AppendLine($"[yellow]Encoding:[/] {string.Join(" ", Encoding)}");
        sb.AppendLine($"[yellow]EncodingSize:[/] {string.Join(" ", EncodingSize)}");
        sb.AppendLine($"[yellow]Size:[/] {string.Join(" ", Size)}");
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
        sb.AppendLine($"[yellow]InstallSize:[/] {string.Join(" ", InstallSize)}");
        sb.AppendLine($"[yellow]DownloadSize:[/] {string.Join(" ", DownloadSize)}");
        sb.AppendLine($"[yellow]PartialPriority:[/] {PartialPriority}");
        sb.AppendLine($"[yellow]PartialPrioritySize:[/] {PartialPrioritySize}");
        sb.AppendLine($"[yellow]BuildSignatureFile:[/] {BuildSignatureFile}");
        sb.AppendLine($"[yellow]PatchIndex:[/] {string.Join(" ", PatchIndex)}");
        sb.AppendLine($"[yellow]PatchIndexSize:[/] {string.Join(" ", PatchIndexSize)}");
        return sb.ToString();
    }
}