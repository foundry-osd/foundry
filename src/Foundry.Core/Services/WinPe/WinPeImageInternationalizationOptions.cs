namespace Foundry.Core.Services.WinPe;

public sealed record WinPeImageInternationalizationOptions
{
    public string MountedImagePath { get; init; } = string.Empty;
    public WinPeArchitecture Architecture { get; init; }
    public WinPeToolPaths? Tools { get; init; }
    public string WinPeLanguage { get; init; } = string.Empty;
    public string WorkingDirectoryPath { get; init; } = string.Empty;
    public IProgress<WinPeDismProgress>? DismProgress { get; init; }
}
