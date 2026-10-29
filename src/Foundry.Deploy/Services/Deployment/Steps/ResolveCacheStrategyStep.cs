// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Deploy.Services.Cache;
using Foundry.Deploy.Services.Hardware;
using Foundry.Deploy.Services.Logging;

namespace Foundry.Deploy.Services.Deployment.Steps;

public sealed class ResolveCacheStrategyStep : DeploymentStepBase
{
    private readonly ICacheLocatorService _cacheLocatorService;
    private readonly ITargetDiskService _targetDiskService;
    private readonly INetworkCacheSessionAccessor? _networkCacheAccessor;

    public ResolveCacheStrategyStep(
        ICacheLocatorService cacheLocatorService,
        ITargetDiskService targetDiskService,
        INetworkCacheSessionAccessor? networkCacheAccessor = null)
    {
        _cacheLocatorService = cacheLocatorService;
        _targetDiskService = targetDiskService;
        _networkCacheAccessor = networkCacheAccessor;
    }

    public override string Name => DeploymentStepNames.ResolveCacheStrategy;

    protected override async Task<DeploymentStepResult> ExecuteLiveAsync(DeploymentStepExecutionContext context, CancellationToken cancellationToken)
    {
        context.EmitCurrentStepIndeterminate("Resolving cache strategy...", "Resolving cache location...", DeploymentOperationNames.ResolveCache);
        CacheResolution cache = await _cacheLocatorService
            .ResolveAsync(context.Request.Mode, context.Request.CacheRootPath, cancellationToken)
            .ConfigureAwait(false);

        context.EmitCurrentStepIndeterminate("Resolving cache strategy...", "Checking cache disk conflict...", DeploymentOperationNames.ValidateCacheTargetDisk);
        cache = await AdjustCacheForTargetDiskConflictAsync(cache, context, cancellationToken).ConfigureAwait(false);
        context.RuntimeState.ResolvedCache = cache;
        await context.AppendLogAsync(DeploymentLogLevel.Info, $"Cache resolved: {cache.RootPath} ({cache.Source})", cancellationToken).ConfigureAwait(false);
        context.EnsureWorkspaceFolders();
        await ApplyNetworkCacheSessionAsync(context, string.Empty, cancellationToken).ConfigureAwait(false);

        return DeploymentStepResult.Succeeded("Cache strategy resolved.");
    }

    protected override async Task<DeploymentStepResult> ExecuteDryRunAsync(DeploymentStepExecutionContext context, CancellationToken cancellationToken)
    {
        context.EmitCurrentStepIndeterminate("Resolving cache strategy...", "Resolving cache location...", DeploymentOperationNames.ResolveCache);
        CacheResolution cache = await _cacheLocatorService
            .ResolveAsync(context.Request.Mode, context.Request.CacheRootPath, cancellationToken)
            .ConfigureAwait(false);

        context.EmitCurrentStepIndeterminate("Resolving cache strategy...", "Checking cache disk conflict...", DeploymentOperationNames.ValidateCacheTargetDisk);
        cache = await AdjustCacheForTargetDiskConflictAsync(cache, context, cancellationToken).ConfigureAwait(false);
        context.RuntimeState.ResolvedCache = cache;
        context.EnsureWorkspaceFolders();
        await context.AppendLogAsync(DeploymentLogLevel.Info, $"[DRY-RUN] Cache resolved: {cache.RootPath} ({cache.Source})", cancellationToken).ConfigureAwait(false);
        await ApplyNetworkCacheSessionAsync(context, "[DRY-RUN] ", cancellationToken).ConfigureAwait(false);
        await Task.Delay(120, cancellationToken).ConfigureAwait(false);

        return DeploymentStepResult.Succeeded("Cache strategy resolved (simulation).");
    }

    /// <summary>
    /// Records the share of the network cache session resolved at Deploy startup in the runtime state, before the target
    /// disk is prepared, so preflight and the separation checks see imported content found on the share. The step never
    /// resolves the share itself: only the startup check applies the published index, so a session it did not produce is
    /// never used. A dry run records the same share without reading it, so the simulated plan follows the live order.
    /// Share problems never fail the step; downloads then behave as without a network cache.
    /// </summary>
    private async Task ApplyNetworkCacheSessionAsync(DeploymentStepExecutionContext context, string logPrefix, CancellationToken cancellationToken)
    {
        if (_networkCacheAccessor is null)
        {
            return;
        }

        NetworkCacheSession? session = _networkCacheAccessor.Current;
        context.RuntimeState.NetworkCacheSharePath = session is { IsAvailable: true } ? session.SharePath : null;
        string message = session switch
        {
            null => "Network cache not used (not resolved at startup)",
            { IsAvailable: true } => $"Network cache: {session.SharePath} ({(session.CanWrite ? "read and write" : "read only")})",
            _ => $"Network cache not used ({session.Outcome})"
        };
        await context.AppendLogAsync(DeploymentLogLevel.Info, logPrefix + message, cancellationToken).ConfigureAwait(false);
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
        throw PreflightDeploymentStep.Guard("Preflight.CacheUnavailable", "cache_on_target_disk");
    }
}
