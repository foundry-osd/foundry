using Foundry.Deploy.Services.Cache;
using Foundry.Deploy.Services.Hardware;
using Foundry.Deploy.Services.Logging;

namespace Foundry.Deploy.Services.Deployment.Steps;

public sealed class ResolveCacheStrategyStep : DeploymentStepBase
{
    private readonly ICacheLocatorService _cacheLocatorService;
    private readonly ITargetDiskService _targetDiskService;

    public ResolveCacheStrategyStep(ICacheLocatorService cacheLocatorService, ITargetDiskService targetDiskService)
    {
        _cacheLocatorService = cacheLocatorService;
        _targetDiskService = targetDiskService;
    }

    public override int Order => 4;

    public override string Name => DeploymentStepNames.ResolveCacheStrategy;

    protected override async Task<DeploymentStepResult> ExecuteLiveAsync(DeploymentStepExecutionContext context, CancellationToken cancellationToken)
    {
        context.EmitCurrentStepIndeterminate("Resolving cache strategy...", "Resolving cache location...");
        CacheResolution cache = await _cacheLocatorService
            .ResolveAsync(context.Request.Mode, context.Request.CacheRootPath, cancellationToken)
            .ConfigureAwait(false);

        context.EmitCurrentStepIndeterminate("Resolving cache strategy...", "Checking cache disk conflict...");
        cache = await AdjustCacheForTargetDiskConflictAsync(cache, context, cancellationToken).ConfigureAwait(false);
        context.RuntimeState.ResolvedCache = cache;
        await context.AppendLogAsync(DeploymentLogLevel.Info, $"Cache resolved: {cache.RootPath} ({cache.Source})", cancellationToken).ConfigureAwait(false);
        context.EnsureWorkspaceFolders();

        return DeploymentStepResult.Succeeded("Cache strategy resolved.");
    }

    protected override async Task<DeploymentStepResult> ExecuteDryRunAsync(DeploymentStepExecutionContext context, CancellationToken cancellationToken)
    {
        context.EmitCurrentStepIndeterminate("Resolving cache strategy...", "Resolving cache location...");
        CacheResolution cache = await _cacheLocatorService
            .ResolveAsync(context.Request.Mode, context.Request.CacheRootPath, cancellationToken)
            .ConfigureAwait(false);

        context.EmitCurrentStepIndeterminate("Resolving cache strategy...", "Checking cache disk conflict...");
        cache = await AdjustCacheForTargetDiskConflictAsync(cache, context, cancellationToken).ConfigureAwait(false);
        context.RuntimeState.ResolvedCache = cache;
        context.EnsureWorkspaceFolders();
        await context.AppendLogAsync(DeploymentLogLevel.Info, $"[DRY-RUN] Cache resolved: {cache.RootPath} ({cache.Source})", cancellationToken).ConfigureAwait(false);
        await Task.Delay(120, cancellationToken).ConfigureAwait(false);

        return DeploymentStepResult.Succeeded("Cache strategy resolved (simulation).");
    }

    private async Task<CacheResolution> AdjustCacheForTargetDiskConflictAsync(
        CacheResolution resolvedCache,
        DeploymentStepExecutionContext context,
        CancellationToken cancellationToken)
    {
        int? cacheDiskNumber = await _targetDiskService
            .GetDiskNumberForPathAsync(resolvedCache.RootPath, cancellationToken)
            .ConfigureAwait(false);

        if (!cacheDiskNumber.HasValue || cacheDiskNumber.Value != context.Request.TargetDiskNumber)
        {
            return resolvedCache;
        }

        string message =
            $"Cache conflict: cache path '{resolvedCache.RootPath}' is on target disk {context.Request.TargetDiskNumber}. " +
            "Deployment is blocked to avoid writing deployment cache on the destination disk.";
        await context.AppendLogAsync(DeploymentLogLevel.Error, message, cancellationToken).ConfigureAwait(false);
        throw new InvalidOperationException(message);
    }
}
