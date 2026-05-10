namespace Foundry.Core.Services.WinPe;

public sealed record WinPeLanguageDiscoveryOptions
{
    public WinPeArchitecture Architecture { get; init; } = WinPeArchitecture.X64;
    public WinPeToolPaths? Tools { get; init; }
}
