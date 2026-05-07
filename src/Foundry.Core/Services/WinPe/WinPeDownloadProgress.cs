namespace Foundry.Core.Services.WinPe;

public sealed record WinPeDownloadProgress
{
    public int? Percent { get; init; }

    public string Status { get; init; } = string.Empty;
}
