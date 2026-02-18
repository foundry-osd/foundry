namespace Foundry.Services.WinPe;

public sealed record WinPeBuildOptions
{
    public string SourceDirectoryPath { get; init; } = string.Empty;
    public string OutputDirectoryPath { get; init; } = string.Empty;
    public string? WorkingDirectoryPath { get; init; }
    public string? AdkRootPath { get; init; }
    public bool CleanExistingWorkingDirectory { get; init; } = true;
    public WinPeArchitecture Architecture { get; init; } = WinPeArchitecture.X64;
    public WinPeSignatureMode SignatureMode { get; init; } = WinPeSignatureMode.Pca2023;
}
