using System.Net.Http.Headers;
using System.Text;
using Spectre.Console;

namespace CASInstaller;

public abstract class CDN
{
    string Product { get; set; }
    public string Name { get; set; }
    public string Path { get; set; }
    public string[] Hosts { get; set; }
    public string[] Servers { get; set; }
    public string ConfigPath { get; set; }

    protected CDN(string? product)
    {
        Product = product;
    }

    public abstract Task<byte[]?> GetCDNConfig(Hash key);
    public abstract Task<byte[]?> GetConfig(Hash key);
    public abstract Task<byte[]?> GetData(Hash key);
    public abstract Task<byte[]?> GetData(Hash key, int start, int size);
    public abstract Task<byte[]> GetPatch(Hash key);

    public override string ToString()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"[yellow]Product:[/] {Product}");
        sb.AppendLine($"[yellow]Name:[/] {Name}");
        sb.AppendLine($"[yellow]Path:[/] {Path}");
        sb.AppendLine($"[yellow]Hosts:[/]");

        foreach (var host in Hosts)
        {
            sb.AppendLine($"  {host}");
        }

        sb.AppendLine($"[yellow]Servers:[/]");

        foreach (var server in Servers)
        {
            sb.AppendLine($"  {server}");
        }

        sb.AppendLine($"[yellow]ConfigPath:[/] {ConfigPath}");
        return sb.ToString();
    }

    public void LogInfo()
    {
        AnsiConsole.MarkupLine("[bold blue]---------------[/]");
        AnsiConsole.MarkupLine("[bold blue]----- CDN -----[/]");
        AnsiConsole.MarkupLine("[bold blue]---------------[/]");
        AnsiConsole.Markup(this.ToString());
    }
}
