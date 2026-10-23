// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Deploy.Services.Cache;
using Foundry.Deploy.Services.Hardware;
using Foundry.Deploy.Services.Logging;
using Foundry.Utilities.Diagnostics;

namespace Foundry.Deploy.Services.Deployment.Steps;

public sealed class ResolveCacheStrategyStep : DeploymentStepBase
{
    private readonly ICacheLocatorService _cacheLocatorService;
    private readonly ITargetDiskService _targetDiskService;
    private readonly INetworkCacheSessionResolver? _networkCacheResolver;
    private readonly INetworkCacheSessionAccessor? _networkCacheAccessor;

    public ResolveCacheStrategyStep(
        ICacheLocatorService cacheLocatorService,
        ITargetDiskService targetDiskService,
        INetworkCacheSessionResolver? networkCacheResolver = null,
        INetworkCacheSessionAccessor? networkCacheAccessor = null)
    {
        _cacheLocatorService = cacheLocatorService;
        _targetDiskService = targetDiskService;
        _networkCacheResolver = networkCacheResolver;
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
        await ResolveNetworkCacheAsync(context, cancellationToken).ConfigureAwait(false);

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
        await Task.Delay(120, cancellationToken).ConfigureAwait(false);

        return DeploymentStepResult.Succeeded("Cache strategy resolved (simulation).");
    }

    /// <summary>
    /// Resolves the network cache before the target disk is prepared, so imported content found on the share can be
    /// checked by preflight. Share problems never fail the step; downloads then behave as without a network cache.
    /// </summary>
    private async Task ResolveNetworkCacheAsync(DeploymentStepExecutionContext context, CancellationToken cancellationToken)
    {
        if (_networkCacheResolver is null || _networkCacheAccessor is null)
        {
            return;
        }

        context.EmitCurrentStepIndeterminate("Resolving cache strategy...", "Resolving cache location...", DeploymentOperationNames.ResolveCache);
        NetworkCacheSession session = await _networkCacheResolver
            .ResolveAsync(context.RuntimeState.WorkspaceRoot, DiagnosticSessionContext.CurrentSessionId, cancellationToken)
            .ConfigureAwait(false);
        _networkCacheAccessor.Current = session;
        string message = session.IsAvailable
            ? $"Network cache: {session.SharePath} ({(session.CanWrite ? "read and write" : "read only")})"
            : $"Network cache not used ({session.Outcome})";
        await context.AppendLogAsync(DeploymentLogLevel.Info, message, cancellationToken).ConfigureAwait(false);
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
