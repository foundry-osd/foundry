namespace Foundry.Deploy.Services.DriverPacks;

public interface IDriverPackExtractionService
{
    Task<DriverPackExtractionResult> ExtractAsync(
        DriverPackExecutionPlan executionPlan,
        string extractionRootPath,
        CancellationToken cancellationToken = default,
        IProgress<double>? progress = null);
}
