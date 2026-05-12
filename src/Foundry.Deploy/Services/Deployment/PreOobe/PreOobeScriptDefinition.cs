namespace Foundry.Deploy.Services.Deployment.PreOobe;

/// <summary>
/// Describes an embedded PowerShell script that must be staged for pre-OOBE execution.
/// </summary>
/// <remarks>
/// Provisioning keeps one definition per <see cref="Id"/>, orders scripts by
/// <see cref="Priority"/>, and uses <see cref="Id"/> as the deterministic tie-breaker.
/// </remarks>
public sealed record PreOobeScriptDefinition
{
    /// <summary>
    /// Gets the stable script identifier used for de-duplication and deterministic ordering.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Gets the target PowerShell file name written under the staged pre-OOBE script folder.
    /// </summary>
    public required string FileName { get; init; }

    /// <summary>
    /// Gets the embedded manifest resource name that contains the script content.
    /// </summary>
    public required string ResourceName { get; init; }

    /// <summary>
    /// Gets the execution priority used by the generated pre-OOBE runner.
    /// </summary>
    public required PreOobeScriptPriority Priority { get; init; }

    /// <summary>
    /// Gets the PowerShell arguments passed to the script by the generated runner.
    /// </summary>
    public IReadOnlyList<string> Arguments { get; init; } = [];
}
