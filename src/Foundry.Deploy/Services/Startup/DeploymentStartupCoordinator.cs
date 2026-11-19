// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO;
using System.Net.Http;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text.Json;
using Foundry.Deploy.Models;
using Foundry.Deploy.Models.Configuration;
using Foundry.Deploy.Services.Autopilot;
using Foundry.Deploy.Services.Cache;
using Foundry.Deploy.Services.Catalog;
using Foundry.Deploy.Services.Configuration;
using Foundry.Deploy.Services.Hardware;
using Foundry.Deploy.Services.Runtime;
using Foundry.Deploy.Services.System;
using Foundry.Utilities.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Foundry.Deploy.Services.Startup;

public sealed class DeploymentStartupCoordinator : IDeploymentStartupCoordinator
{
    private const string WinPeTransientRuntimeRoot = @"X:\Foundry\Runtime";
    private static readonly TimeSpan GroupTagDiscoveryTimeout = TimeSpan.FromSeconds(30);

    private readonly IDeployConfigurationService _deployConfigurationService;
    private readonly IAutopilotProfileCatalogService _autopilotProfileCatalogService;
    private readonly IHardwareProfileService _hardwareProfileService;
    private readonly IOfflineWindowsComputerNameService _offlineWindowsComputerNameService;
    private readonly ITargetDiskService _targetDiskService;
    private readonly IDeploymentCatalogLoadService _deploymentCatalogLoadService;
    private readonly IAutopilotGroupTagDiscoveryService _autopilotGroupTagDiscoveryService;
    private readonly ILogger<DeploymentStartupCoordinator> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly INetworkCacheSessionResolver? _networkCacheResolver;
    private readonly INetworkCacheSessionAccessor? _networkCacheAccessor;
    private readonly NetworkCacheStartupCheck _networkCacheStartupCheck;
    private StartupState? _lastStartup;

    public DeploymentStartupCoordinator(
        IDeployConfigurationService deployConfigurationService,
        IAutopilotProfileCatalogService autopilotProfileCatalogService,
        IHardwareProfileService hardwareProfileService,
        IOfflineWindowsComputerNameService offlineWindowsComputerNameService,
        ITargetDiskService targetDiskService,
        IDeploymentCatalogLoadService deploymentCatalogLoadService,
        IAutopilotGroupTagDiscoveryService autopilotGroupTagDiscoveryService,
        ILogger<DeploymentStartupCoordinator> logger,
        TimeProvider? timeProvider = null,
        INetworkCacheSessionResolver? networkCacheResolver = null,
        INetworkCacheSessionAccessor? networkCacheAccessor = null,
        NetworkCacheStartupCheck? networkCacheStartupCheck = null)
    {
        _deployConfigurationService = deployConfigurationService;
        _autopilotProfileCatalogService = autopilotProfileCatalogService;
        _hardwareProfileService = hardwareProfileService;
        _offlineWindowsComputerNameService = offlineWindowsComputerNameService;
        _targetDiskService = targetDiskService;
        _deploymentCatalogLoadService = deploymentCatalogLoadService;
        _autopilotGroupTagDiscoveryService = autopilotGroupTagDiscoveryService;
        _logger = logger;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _networkCacheResolver = networkCacheResolver;
        _networkCacheAccessor = networkCacheAccessor;
        _networkCacheStartupCheck = networkCacheStartupCheck ?? new NetworkCacheStartupCheck();
    }

    /// <inheritdoc />
    public async Task<DeploymentStartupSnapshot> InitializeAsync(DeploymentStartupRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        DeployConfigurationLoadResult deployConfigLoadResult = _deployConfigurationService.LoadOptional();
        IReadOnlyList<AutopilotProfileCatalogItem> autopilotProfiles = _autopilotProfileCatalogService.LoadAvailableProfiles();
        string cacheRootPath = ResolveCacheRootPath(request.RuntimeContext, request.IsDebugSafeMode);
        AutopilotGroupTagRefresh? groupTags = deployConfigLoadResult.Document is null
            ? null
            : await RefreshAutopilotGroupTagsAsync(deployConfigLoadResult.Document, cacheRootPath, cancellationToken).ConfigureAwait(false);
        FoundryDeployConfigurationDocument? deployConfigurationDocument = groupTags?.Document;
        _lastStartup = new StartupState(request, cacheRootPath, deployConfigLoadResult.Document, deployConfigurationDocument);

        cancellationToken.ThrowIfCancellationRequested();

        Task<string> computerNameTask = ResolveComputerNameAsync(request.FallbackComputerName);
        Task<HardwareLoadResult> hardwareTask = LoadHardwareAsync();
        Task<TargetDiskLoadResult> targetDisksTask = LoadTargetDisksAsync();
        Task<CatalogLoadResult> catalogTask = LoadCatalogsAsync(cancellationToken);
        Task<NetworkCacheStartupResult?> networkCacheTask = ResolveNetworkCacheAsync(request, cacheRootPath, deployConfigurationDocument, cancellationToken);

        await Task.WhenAll(computerNameTask, hardwareTask, targetDisksTask, catalogTask, networkCacheTask).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        NetworkCacheStartupResult? networkCache = networkCacheTask.Result;
        deployConfigurationDocument = networkCache?.Document ?? deployConfigurationDocument;

        DeployMachineNamingSettings machineNaming = deployConfigurationDocument?.Customization.MachineNaming
            ?? new DeployMachineNamingSettings();
        MachineNamePreparationResult machineName = MachineNamePreparationService.Prepare(
            machineNaming,
            computerNameTask.Result,
            hardwareTask.Result.Profile);
        if (!machineName.IsSuccess)
        {
            _logger.LogWarning(
                "Machine name preparation failed. FailureKind={FailureKind}, ComponentType={ComponentType}",
                machineName.FailureKind,
                machineName.ComponentType);
        }

        return new DeploymentStartupSnapshot
        {
            ConfigurationPath = deployConfigLoadResult.ConfigurationPath,
            ConfigurationFailureMessage = deployConfigLoadResult.FailureMessage,
            CacheRootPath = cacheRootPath,
            DeployConfigurationDocument = deployConfigurationDocument,
            AutopilotGroupTagsFailed = groupTags?.Failed == true,
            NetworkCacheFailure = networkCache?.Failure,
            BootMediaUpdateReason = deployConfigLoadResult.BootMediaUpdateReason,
            LatestReleaseCheck = deployConfigLoadResult.LatestReleaseCheck,
            AutopilotProfiles = autopilotProfiles,
            MachineNamePreparation = machineName,
            DetectedHardware = hardwareTask.Result.Profile,
            HardwareDetectionFailureMessage = hardwareTask.Result.ErrorMessage,
            TargetDisks = targetDisksTask.Result.Disks,
            CatalogSnapshot = catalogTask.Result.Snapshot,
            CatalogFailure = catalogTask.Result.Failure
        };
    }

    /// <inheritdoc />
    public async Task<DeploymentCatalogSnapshot> ReloadCatalogsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CatalogLoadResult result = await LoadCatalogsAsync(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (result.Failure is { } failure)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }

        return result.Snapshot;
    }

    /// <inheritdoc />
    public Task<AutopilotGroupTagRefresh> RefreshAutopilotGroupTagsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_lastStartup is not { LoadedDocument: { } document } startup)
        {
            throw new InvalidOperationException("Startup has not loaded a Deploy configuration.");
        }

        return RefreshAutopilotGroupTagsAsync(document, startup.CacheRootPath, cancellationToken);
    }

    /// <inheritdoc />
    public bool ShouldResolveNetworkCacheAgain()
    {
        if (_networkCacheResolver is null || _networkCacheAccessor?.Current is not { } session || session.Outcome == NetworkCacheOutcome.Disabled)
        {
            return false;
        }

        // Compared as sets: adapters may list the same addresses in another order.
        return !session.IsAvailable || !session.DeviceAddresses.ToHashSet().SetEquals(_networkCacheResolver.ReadLocalAddresses());
    }

    /// <inheritdoc />
    public Task<NetworkCacheStartupResult?> ResolveNetworkCacheAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return _lastStartup is { } startup
            ? ResolveNetworkCacheAsync(startup.Request, startup.CacheRootPath, startup.Document, cancellationToken)
            : Task.FromResult<NetworkCacheStartupResult?>(null);
    }

    /// <summary>
    /// Loads the catalogs once. A failure (the downloads already retried) is returned, not thrown, so the window can offer a
    /// retry; the caller's own cancellation is not a failure and is never logged as one. One warning per failed attempt.
    /// </summary>
    private async Task<CatalogLoadResult> LoadCatalogsAsync(CancellationToken cancellationToken)
    {
        try
        {
            return new CatalogLoadResult(await _deploymentCatalogLoadService.LoadAsync().ConfigureAwait(false), null);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Operating system and driver pack catalogs could not be loaded. FailureType={FailureType}", ex.GetType().Name);
            return new CatalogLoadResult(new DeploymentCatalogSnapshot([], []), ex);
        }
        catch (Exception) when (cancellationToken.IsCancellationRequested)
        {
            return new CatalogLoadResult(new DeploymentCatalogSnapshot([], []), null);
        }
    }

    private async Task<string> ResolveComputerNameAsync(string fallbackComputerName)
    {
        try
        {
            string? resolvedName = await _offlineWindowsComputerNameService
                .TryGetOfflineComputerNameAsync()
                .ConfigureAwait(false);

            return !string.IsNullOrWhiteSpace(resolvedName)
                ? resolvedName
                : fallbackComputerName;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load offline Windows computer name during startup.");
            return fallbackComputerName;
        }
    }

    /// <summary>
    /// Connects the network cache share before the wizard opens, and again after a return from the Network page, so content
    /// imported to the share is offered with the media content and the deployment reuses the same session. The published index and its content are checked as part of
    /// share selection: a share whose index is missing, too old or incomplete is skipped for the next share of the scope,
    /// and startup is blocked only when no share passes. Retry runs this whole selection again. The resolver never throws
    /// for share problems. Returns <see langword="null"/> when no session is resolved or no configuration is loaded. A
    /// debug-safe session never touches the share: it neither maps the UNC path nor writes the probe file, and the session
    /// stays unset, so the deployment runs without a network cache.
    /// </summary>
    private async Task<NetworkCacheStartupResult?> ResolveNetworkCacheAsync(DeploymentStartupRequest request, string cacheRootPath,
        FoundryDeployConfigurationDocument? document, CancellationToken cancellationToken)
    {
        if (_networkCacheResolver is null || _networkCacheAccessor is null)
        {
            return null;
        }

        if (request.IsDebugSafeMode)
        {
            _logger.LogInformation("Network cache not resolved: debug-safe mode never connects to the share.");
            return null;
        }

        // A retry after a startup failure, or a return from the Network page, resolves again. The previous session is closed
        // first: disconnecting it afterwards would close the same UNC path the new session may have just connected.
        _networkCacheAccessor.Current?.Dispose();
        _networkCacheAccessor.Current = null;

        string workspaceRootPath = ResolveWorkspaceRootPath(cacheRootPath);
        NetworkCacheStartupResult? selectedShareResult = null;
        Func<NetworkCacheSession, CancellationToken, Task<NetworkCacheStartupFailure?>>? validateShare = document is null
            ? null
            : async (candidate, token) =>
            {
                selectedShareResult = await _networkCacheStartupCheck.RunAsync(document, candidate, token).ConfigureAwait(false);
                return selectedShareResult.Failure;
            };
        NetworkCacheSession session = await _networkCacheResolver
            .ResolveAsync(workspaceRootPath, DiagnosticSessionContext.CurrentSessionId, validateShare, cancellationToken)
            .ConfigureAwait(false);
        _networkCacheAccessor.Current = session;
        if (document is null)
        {
            return null;
        }

        // The selected share's result carries the index sections. Without one, the check reports why the session is
        // unusable, or passes a configuration that does not use a content index.
        return session.IsAvailable && selectedShareResult is { Failure: null }
            ? selectedShareResult
            : await _networkCacheStartupCheck.RunAsync(document, session, cancellationToken).ConfigureAwait(false);
    }

    private async Task<HardwareLoadResult> LoadHardwareAsync()
    {
        try
        {
            HardwareProfile profile = await _hardwareProfileService.GetCurrentAsync().ConfigureAwait(false);
            _logger.LogInformation("Hardware profile loaded during startup. DisplayLabel={DisplayLabel}", profile.DisplayLabel);
            return new HardwareLoadResult(profile, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Hardware profile loading failed during startup.");
            return new HardwareLoadResult(null, $"Hardware detection failed: {ex.Message}");
        }
    }

    private async Task<TargetDiskLoadResult> LoadTargetDisksAsync()
    {
        try
        {
            IReadOnlyList<TargetDiskInfo> disks = await _targetDiskService.GetDisksAsync().ConfigureAwait(false);
            return new TargetDiskLoadResult(disks);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Target disk discovery failed during startup.");
            return new TargetDiskLoadResult([]);
        }
    }

    private static string ResolveCacheRootPath(DeploymentRuntimeContext runtimeContext, bool isDebugSafeMode)
    {
        if (isDebugSafeMode)
        {
            return Path.Combine(Path.GetTempPath(), "Foundry", "Runtime", "Debug");
        }

        if (runtimeContext.Mode == DeploymentMode.Usb)
        {
            return runtimeContext.UsbCacheRuntimeRoot ?? WinPeTransientRuntimeRoot;
        }

        return WinPeTransientRuntimeRoot;
    }

    private async Task<AutopilotGroupTagRefresh> RefreshAutopilotGroupTagsAsync(
        FoundryDeployConfigurationDocument document,
        string cacheRootPath,
        CancellationToken cancellationToken)
    {
        DeployAutopilotHardwareHashUploadSettings hardwareHashUpload = document.Autopilot.HardwareHashUpload;
        if (!CanDiscoverHardwareHashGroupTags(document.Autopilot, hardwareHashUpload))
        {
            return new AutopilotGroupTagRefresh(document, Failed: false);
        }

        // One budget covers authentication, Graph pagination and retry delays; await cleanup before continuing.
        using var timeout = new CancellationTokenSource(GroupTagDiscoveryTimeout, _timeProvider);
        using var discoveryCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        try
        {
            string workspaceRootPath = ResolveWorkspaceRootPath(cacheRootPath);
            IReadOnlyList<string> groupTags = await _autopilotGroupTagDiscoveryService
                .DiscoverAsync(hardwareHashUpload, workspaceRootPath, discoveryCancellation.Token)
                .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            _logger.LogInformation("Discovered {GroupTagCount} Autopilot group tag(s).", groupTags.Count);
            return new AutopilotGroupTagRefresh(WithRuntimeGroupTags(document, groupTags), Failed: false);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Optional Autopilot group tag discovery timed out. Deploy will keep the configured group tags and default.");
            return new AutopilotGroupTagRefresh(document, Failed: true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or FileNotFoundException or CryptographicException or HttpRequestException or JsonException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _logger.LogWarning(ex, "Optional Autopilot group tag discovery failed. Deploy will keep the configured group tags and default.");
            return new AutopilotGroupTagRefresh(document, Failed: true);
        }
    }

    private static bool CanDiscoverHardwareHashGroupTags(
        DeployAutopilotSettings autopilot,
        DeployAutopilotHardwareHashUploadSettings hardwareHashUpload)
    {
        return autopilot.IsEnabled &&
               autopilot.ProvisioningMode == AutopilotProvisioningMode.HardwareHashUpload &&
               !string.IsNullOrWhiteSpace(hardwareHashUpload.TenantId) &&
               !string.IsNullOrWhiteSpace(hardwareHashUpload.ClientId) &&
               !string.IsNullOrWhiteSpace(hardwareHashUpload.ActiveCertificateThumbprint) &&
               hardwareHashUpload.CertificatePfxSecret is not null &&
               hardwareHashUpload.CertificatePfxPasswordSecret is not null &&
               hardwareHashUpload.ActiveCertificateExpiresOnUtc is DateTimeOffset expiresOnUtc &&
               expiresOnUtc > DateTimeOffset.UtcNow;
    }

    private static string ResolveWorkspaceRootPath(string cacheRootPath)
    {
        const string winPeWorkspaceRoot = @"X:\Foundry";
        if (Directory.Exists(winPeWorkspaceRoot))
        {
            return winPeWorkspaceRoot;
        }

        if (Directory.Exists(cacheRootPath))
        {
            DirectoryInfo cacheRoot = new(cacheRootPath);
            if (string.Equals(cacheRoot.Name, "Runtime", StringComparison.OrdinalIgnoreCase) &&
                cacheRoot.Parent is not null)
            {
                return cacheRoot.Parent.FullName;
            }
        }

        return cacheRootPath;
    }

    private static FoundryDeployConfigurationDocument WithRuntimeGroupTags(
        FoundryDeployConfigurationDocument document,
        IReadOnlyList<string> groupTags)
    {
        return document with
        {
            Autopilot = document.Autopilot with
            {
                HardwareHashUpload = document.Autopilot.HardwareHashUpload with
                {
                    KnownGroupTags = groupTags
                }
            }
        };
    }

    /// <summary>What the last startup resolved from, so a return from the Network page can run its network steps again.</summary>
    /// <param name="Request">The startup request.</param>
    /// <param name="CacheRootPath">The cache root the startup resolved.</param>
    /// <param name="LoadedDocument">The configuration as loaded, before the group tag discovery.</param>
    /// <param name="Document">The configuration with the startup's group tags, the one the network cache check ran on.</param>
    private sealed record StartupState(DeploymentStartupRequest Request, string CacheRootPath,
        FoundryDeployConfigurationDocument? LoadedDocument, FoundryDeployConfigurationDocument? Document);

    private sealed record CatalogLoadResult(DeploymentCatalogSnapshot Snapshot, Exception? Failure);
    private sealed record HardwareLoadResult(HardwareProfile? Profile, string? ErrorMessage);
    private sealed record TargetDiskLoadResult(IReadOnlyList<TargetDiskInfo> Disks);
}
