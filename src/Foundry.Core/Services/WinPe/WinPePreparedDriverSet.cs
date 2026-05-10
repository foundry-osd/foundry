namespace Foundry.Core.Services.WinPe;

public sealed record WinPePreparedDriverSet
{
    public IReadOnlyList<string> ExtractionDirectories { get; init; } = [];
    public IReadOnlyList<string> DownloadedPackagePaths { get; init; } = [];
}
