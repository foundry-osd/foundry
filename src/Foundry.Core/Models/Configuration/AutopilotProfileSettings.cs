namespace Foundry.Core.Models.Configuration;

public sealed record AutopilotProfileSettings
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public required string FolderName { get; init; }
    public required string Source { get; init; }
    public required DateTimeOffset ImportedAtUtc { get; init; }
    public required string JsonContent { get; init; }
}
