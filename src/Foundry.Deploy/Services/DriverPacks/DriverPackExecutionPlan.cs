namespace Foundry.Deploy.Services.DriverPacks;

public sealed record DriverPackExecutionPlan
{
    public required DriverPackInstallMode InstallMode { get; init; }
    public required DriverPackExtractionMethod ExtractionMethod { get; init; }
    public required DeferredDriverPackageCommandKind DeferredCommandKind { get; init; }
    public required string DownloadedPath { get; init; }
    public required string EffectiveFileExtension { get; init; }
    public required string Manufacturer { get; init; }
    public required bool RequiresInfPayload { get; init; }
}
