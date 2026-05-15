namespace Foundry.Core.Services.WinPe;

public sealed record WinPeMediaProgress
{
    public int Percent { get; init; }
    public string Status { get; init; } = string.Empty;
    public string LogDetail { get; init; } = string.Empty;
}
