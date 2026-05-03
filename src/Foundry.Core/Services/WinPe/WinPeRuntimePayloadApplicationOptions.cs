namespace Foundry.Core.Services.WinPe;

public sealed record WinPeRuntimePayloadApplicationOptions
{
    public bool IsEnabled { get; init; }
    public string ArchivePath { get; init; } = string.Empty;
    public string ProjectPath { get; init; } = string.Empty;
}
