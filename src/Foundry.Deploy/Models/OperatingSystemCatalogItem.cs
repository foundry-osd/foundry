namespace Foundry.Deploy.Models;

public sealed record OperatingSystemCatalogItem
{
    public string SourceId { get; init; } = string.Empty;
    public string ClientType { get; init; } = string.Empty;
    public string WindowsRelease { get; init; } = string.Empty;
    public string ReleaseId { get; init; } = string.Empty;
    public string Build { get; init; } = string.Empty;
    public int BuildMajor { get; init; }
    public int BuildUbr { get; init; }
    public string Architecture { get; init; } = string.Empty;
    public string LanguageCode { get; init; } = string.Empty;
    public string Language { get; init; } = string.Empty;
    public string Edition { get; init; } = string.Empty;
    public string FileName { get; init; } = string.Empty;
    public long SizeBytes { get; init; }
    public string LicenseChannel { get; init; } = string.Empty;
    public string Url { get; init; } = string.Empty;

    public string DisplayLabel =>
        $"Windows {WindowsRelease} {ReleaseId} | {Architecture} | {LanguageCode} | {Edition} | {LicenseChannel} | {Build}";
}
