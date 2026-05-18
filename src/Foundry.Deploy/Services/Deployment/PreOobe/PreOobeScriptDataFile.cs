namespace Foundry.Deploy.Services.Deployment.PreOobe;

/// <summary>
/// Describes a generated data file staged next to pre-OOBE scripts.
/// </summary>
public sealed record PreOobeScriptDataFile
{
    /// <summary>
    /// Gets the target file name written under the staged pre-OOBE data folder.
    /// </summary>
    public required string FileName { get; init; }

    /// <summary>
    /// Gets the UTF-8 file content.
    /// </summary>
    public required string Content { get; init; }
}
