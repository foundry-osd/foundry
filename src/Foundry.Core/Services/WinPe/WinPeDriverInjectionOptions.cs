namespace Foundry.Core.Services.WinPe;

public sealed record WinPeDriverInjectionOptions
{
    public string MountedImagePath { get; init; } = string.Empty;
    public IReadOnlyList<string> DriverPackagePaths { get; init; } = [];
    public bool RecurseSubdirectories { get; init; } = true;
    public string? DismExecutablePath { get; init; }
    public string WorkingDirectoryPath { get; init; } = string.Empty;
}
