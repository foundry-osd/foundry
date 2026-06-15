namespace Foundry.Core.Models.Configuration;

/// <summary>
/// Describes whether Foundry should prepare the Windows Recovery Environment custom tool payload.
/// </summary>
public sealed record OsRecoverySettings
{
    /// <summary>
    /// Gets a value indicating whether the OS recovery payload should be generated.
    /// </summary>
    public bool IsEnabled { get; init; }
}
