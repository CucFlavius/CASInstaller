namespace CASInstaller;

public class BuildInfo
{
    public List<Build> Builds { get; set; }

    public class Build
    {
        public string? Branch { get; set; }
        public byte Active { get; set; }
        public Hash BuildKey { get; set; }
        public Hash CDNKey { get; set; }
        public Hash InstallKey { get; set; }
        public int IMSize { get; set; }
        public string? CDNPath { get; set; }
        public string[]? CDNHosts { get; set; }
        public string[]? CDNServers { get; set; }
        public string[]? Tags { get; set; }
        public string? Armadillo { get; set; }
        public string? LastActivated { get; set; }
        public string? Version { get; set; }
        public Hash KeyRing { get; set; }
        public string? Product { get; set; }

        private static readonly string[] TableHeader =
        [
            "Branch!STRING:0",
            "Active!DEC:1",
            "Build Key!HEX:16",
            "CDN Key!HEX:16",
            "Install Key!HEX:16",
            "IM Size!DEC:4",
            "CDN Path!STRING:0",
            "CDN Hosts!STRING:0",
            "CDN Servers!STRING:0",
            "Tags!STRING:0",
            "Armadillo!STRING:0",
            "Last Activated!STRING:0",
            "Version!STRING:0",
            "KeyRing!HEX:16",
            "Product!STRING:0"
        ];
        
        public static void WriteTableHeader(TextWriter writer)
        {
            writer.WriteLine(string.Join('|', TableHeader));
        }
        
        public Build() {}
        
        public Build(string? data)
        {
            ArgumentNullException.ThrowIfNull(data);

            string?[] parts = data.Split('|');
            
            Branch = parts[0];
            Active = byte.Parse(parts[1] ?? "0");
            BuildKey = new Hash(parts[2]);
            CDNKey = new Hash(parts[3]);
            InstallKey = new Hash(parts[4]);
            IMSize = parts[5] == string.Empty ? -1 : int.Parse(parts[5] ?? "-1");
            CDNPath = parts[6];
            CDNHosts = parts[7]?.Split(' ');
            CDNServers = parts[8]?.Split(' ');
            Tags = parts[9]?.Split(' ');
            Armadillo = parts[10];
            LastActivated = parts[11];
            Version = parts[12];
            KeyRing = new Hash(parts[13]);
            Product = parts[14];
        }
        
        public void Write(TextWriter writer)
        {
            writer.WriteLine(string.Join('|', new object[]
            {
                Branch ?? string.Empty,
                Active,
                BuildKey.KeyString?.ToLower() ?? string.Empty,
                CDNKey.KeyString?.ToLower() ?? string.Empty,
                InstallKey.KeyString?.ToLower() ?? string.Empty,
                IMSize > 0 ? IMSize : string.Empty,
                CDNPath ?? string.Empty,
                string.Join(' ', CDNHosts!),
                string.Join(' ', CDNServers!),
                string.Join(' ', Tags!),
                Armadillo ?? string.Empty,
                LastActivated ?? string.Empty,
                Version ?? string.Empty,
                KeyRing.KeyString?.ToLower() ?? string.Empty,
                Product ?? string.Empty
            }));
        }
    }
    
    public BuildInfo(string path)
    {
        Builds = [];

        if (!File.Exists(path)) return;
        
        using var reader = new StreamReader(path);
        // Skip first line containing the table header
        reader.ReadLine();
        while (!reader.EndOfStream)
        {
            var build = new Build(reader.ReadLine());
            AddBuild(build);
        }
    }
    
    public void Write(string path)
    {
        using var writer = new StreamWriter(path);
        Build.WriteTableHeader(writer);
        foreach (var build in Builds)
        {
            build.Write(writer);
        }
    }

    public void AddBuild(Build build)
    {
        // Verify if we already have a build with this Branch and Product
        if (Builds.Any(b => b.Branch == build.Branch && b.Product == build.Product))
        {
            // Override the existing build with the new one
            Builds[Builds.FindIndex(b => b.Branch == build.Branch && b.Product == build.Product)] = build;
        }
        else
        {
            Builds.Add(build);
        }
    }
}