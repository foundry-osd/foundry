namespace Foundry.Services.WinPe;

internal sealed record WinPeMountedImageCustomizationProgress
{
    public required int Percent { get; init; }
    public string? Status { get; init; }
}
