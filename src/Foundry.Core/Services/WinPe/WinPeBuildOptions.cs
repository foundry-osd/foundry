namespace Foundry.Core.Services.WinPe;

/// <summary>
/// Describes the source paths and ADK settings used to create a WinPE workspace.
/// </summary>
public sealed record WinPeBuildOptions
{
    /// <summary>
    /// Gets the source directory used as an ADK root hint when no explicit ADK root is provided.
    /// </summary>
    public string SourceDirectoryPath { get; init; } = string.Empty;

    /// <summary>
    /// Gets the output directory that receives the generated workspace.
    /// </summary>
    public string OutputDirectoryPath { get; init; } = string.Empty;

    /// <summary>
    /// Gets an optional fixed workspace path.
    /// </summary>
    public string? WorkingDirectoryPath { get; init; }

    /// <summary>
    /// Gets an optional ADK root path override.
    /// </summary>
    public string? AdkRootPath { get; init; }

    /// <summary>
    /// Gets whether an existing workspace path should be deleted before creation.
    /// </summary>
    public bool CleanExistingWorkingDirectory { get; init; } = true;

    /// <summary>
    /// Gets the WinPE architecture to create.
    /// </summary>
    public WinPeArchitecture Architecture { get; init; } = WinPeArchitecture.X64;

    /// <summary>
    /// Gets the boot signature mode passed to later media creation stages.
    /// </summary>
    public WinPeSignatureMode SignatureMode { get; init; } = WinPeSignatureMode.Pca2011;
}
