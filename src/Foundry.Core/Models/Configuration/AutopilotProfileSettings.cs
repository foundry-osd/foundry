namespace Foundry.Core.Models.Configuration;

/// <summary>
/// Represents an offline Autopilot profile known to Foundry.
/// </summary>
public sealed record AutopilotProfileSettings
{
    /// <summary>
    /// Gets the stable profile ID from Graph or a deterministic manual import ID.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Gets the display name shown to users.
    /// </summary>
    public required string DisplayName { get; init; }

    /// <summary>
    /// Gets the folder-safe name used when staging the profile JSON.
    /// </summary>
    public required string FolderName { get; init; }

    /// <summary>
    /// Gets the source label, such as tenant download or file import.
    /// </summary>
    public required string Source { get; init; }

    /// <summary>
    /// Gets the UTC timestamp when the profile was imported into Foundry.
    /// </summary>
    public required DateTimeOffset ImportedAtUtc { get; init; }

    /// <summary>
    /// Gets the offline Autopilot JSON content.
    /// </summary>
    public required string JsonContent { get; init; }
}
