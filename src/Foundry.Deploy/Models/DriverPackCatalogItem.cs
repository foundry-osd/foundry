namespace Foundry.Deploy.Models;

public sealed record DriverPackCatalogItem
{
    public string Id { get; init; } = string.Empty;
    public string PackageId { get; init; } = string.Empty;
    public string Manufacturer { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
    public string FileName { get; init; } = string.Empty;
    public string DownloadUrl { get; init; } = string.Empty;
    public long SizeBytes { get; init; }
    public string Format { get; init; } = string.Empty;
    public string Type { get; init; } = string.Empty;
    public DateTimeOffset? ReleaseDate { get; init; }
    public string OsName { get; init; } = string.Empty;
    public string OsArchitecture { get; init; } = string.Empty;
    public IReadOnlyList<string> ModelNames { get; init; } = Array.Empty<string>();
    public string Sha256 { get; init; } = string.Empty;

    public string DisplayLabel =>
        $"{Manufacturer} | {Name} | {OsName} {OsArchitecture}";
}
