namespace Foundry.Core.Services.WinPe;

public sealed record WinPeMountedImageCustomizationProgress
{
    public int Percent { get; init; }
    public string Status { get; init; } = string.Empty;
    public int? DetailPercent { get; init; }
    public string DetailStatus { get; init; } = string.Empty;
}
