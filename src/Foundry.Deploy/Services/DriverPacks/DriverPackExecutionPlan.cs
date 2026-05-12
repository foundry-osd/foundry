namespace Foundry.Deploy.Services.DriverPacks;

/// <summary>
/// Describes how a selected driver pack should be downloaded, extracted, and installed.
/// </summary>
public sealed record DriverPackExecutionPlan
{
    /// <summary>
    /// Gets whether the pack is installed offline, deferred to pre-OOBE, or skipped.
    /// </summary>
    public required DriverPackInstallMode InstallMode { get; init; }

    /// <summary>
    /// Gets the extraction method required for the downloaded package.
    /// </summary>
    public required DriverPackExtractionMethod ExtractionMethod { get; init; }

    /// <summary>
    /// Gets the deferred command kind used by the pre-OOBE driver script.
    /// </summary>
    public required DeferredDriverPackageCommandKind DeferredCommandKind { get; init; }
    public required string DownloadedPath { get; init; }
    public required string EffectiveFileExtension { get; init; }
    public required string Manufacturer { get; init; }
    public required bool RequiresInfPayload { get; init; }
}
