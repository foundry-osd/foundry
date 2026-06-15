using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using Foundry.Deploy.Models;
using Foundry.Deploy.Models.Configuration;
using Foundry.Deploy.Services.Autopilot;
using Foundry.Deploy.Services.Catalog;
using Foundry.Deploy.Services.Configuration;
using Foundry.Deploy.Services.Hardware;
using Foundry.Deploy.Services.Runtime;
using Microsoft.Extensions.Logging;

namespace Foundry.Deploy.Services.Startup;

public sealed class DeploymentStartupCoordinator : IDeploymentStartupCoordinator
{
    private const string WinPeTransientRuntimeRoot = @"X:\Foundry\Runtime";

    private readonly IDeployConfigurationService _deployConfigurationService;
    private readonly IAutopilotProfileCatalogService _autopilotProfileCatalogService;
    private readonly IHardwareProfileService _hardwareProfileService;
    private readonly IOfflineWindowsComputerNameService _offlineWindowsComputerNameService;
    private readonly ITargetDiskService _targetDiskService;
    private readonly IDeploymentCatalogLoadService _deploymentCatalogLoadService;
    private readonly IAutopilotGroupTagDiscoveryService _autopilotGroupTagDiscoveryService;
    private readonly IRecoveryTargetDiskResolver _recoveryTargetDiskResolver;
    private readonly ILogger<DeploymentStartupCoordinator> _logger;

    public DeploymentStartupCoordinator(
        IDeployConfigurationService deployConfigurationService,
        IAutopilotProfileCatalogService autopilotProfileCatalogService,
        IHardwareProfileService hardwareProfileService,
        IOfflineWindowsComputerNameService offlineWindowsComputerNameService,
        ITargetDiskService targetDiskService,
        IDeploymentCatalogLoadService deploymentCatalogLoadService,
        IAutopilotGroupTagDiscoveryService autopilotGroupTagDiscoveryService,
        IRecoveryTargetDiskResolver recoveryTargetDiskResolver,
        ILogger<DeploymentStartupCoordinator> logger)
    {
        _deployConfigurationService = deployConfigurationService;
        _autopilotProfileCatalogService = autopilotProfileCatalogService;
        _hardwareProfileService = hardwareProfileService;
        _offlineWindowsComputerNameService = offlineWindowsComputerNameService;
        _targetDiskService = targetDiskService;
        _deploymentCatalogLoadService = deploymentCatalogLoadService;
        _autopilotGroupTagDiscoveryService = autopilotGroupTagDiscoveryService;
        _recoveryTargetDiskResolver = recoveryTargetDiskResolver;
        _logger = logger;
    }

    public async Task<DeploymentStartupSnapshot> InitializeAsync(DeploymentStartupRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        DeployConfigurationLoadResult deployConfigLoadResult = _deployConfigurationService.LoadOptional();
        IReadOnlyList<AutopilotProfileCatalogItem> autopilotProfiles = _autopilotProfileCatalogService.LoadAvailableProfiles();
        string cacheRootPath = ResolveCacheRootPath(request.RuntimeContext, request.IsDebugSafeMode);
        FoundryDeployConfigurationDocument? deployConfigurationDocument = deployConfigLoadResult.Document is null
            ? null
            : await RefreshAutopilotGroupTagsAsync(deployConfigLoadResult.Document, cacheRootPath).ConfigureAwait(false);

        Task<string> computerNameTask = ResolveComputerNameAsync(request.FallbackComputerName);
        Task<HardwareLoadResult> hardwareTask = LoadHardwareAsync();
        Task<TargetDiskLoadResult> targetDisksTask = LoadTargetDisksAsync(request.RuntimeContext.Mode);
        Task<DeploymentCatalogSnapshot> catalogTask = _deploymentCatalogLoadService.LoadAsync();

        await Task.WhenAll(computerNameTask, hardwareTask, targetDisksTask, catalogTask).ConfigureAwait(false);

        return new DeploymentStartupSnapshot
        {
            CacheRootPath = cacheRootPath,
            DeployConfigurationDocument = deployConfigurationDocument,
            IsBootMediaUpdateRecommended = deployConfigLoadResult.IsBootMediaUpdateRecommended,
            AutopilotProfiles = autopilotProfiles,
            EffectiveComputerName = computerNameTask.Result,
            DetectedHardware = hardwareTask.Result.Profile,
            HardwareDetectionFailureMessage = hardwareTask.Result.ErrorMessage,
            TargetDisks = targetDisksTask.Result.Disks,
            CatalogSnapshot = catalogTask.Result
        };
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

    private async Task<TargetDiskLoadResult> LoadTargetDisksAsync(DeploymentMode mode)
    {
        try
        {
            IReadOnlyList<TargetDiskInfo> disks = await _targetDiskService.GetDisksAsync().ConfigureAwait(false);
            if (mode != DeploymentMode.Recovery)
            {
                return new TargetDiskLoadResult(disks);
            }

            int? recoveryDiskNumber = await _recoveryTargetDiskResolver.ResolveAsync().ConfigureAwait(false);
            if (!recoveryDiskNumber.HasValue)
            {
                _logger.LogWarning("OS Recovery mode could not resolve the active recovery target disk.");
                return new TargetDiskLoadResult([]);
            }

            TargetDiskInfo? recoveryDisk = disks.FirstOrDefault(disk => disk.DiskNumber == recoveryDiskNumber.Value);
            if (recoveryDisk is null)
            {
                _logger.LogWarning("OS Recovery target disk {DiskNumber} was not returned by disk discovery.", recoveryDiskNumber.Value);
                return new TargetDiskLoadResult([]);
            }

            return new TargetDiskLoadResult(
            [
                recoveryDisk with
                {
                    IsSelectable = true,
                    SelectionWarning = string.Empty
                }
            ]);
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

    private async Task<FoundryDeployConfigurationDocument> RefreshAutopilotGroupTagsAsync(
        FoundryDeployConfigurationDocument document,
        string cacheRootPath)
    {
        DeployAutopilotHardwareHashUploadSettings hardwareHashUpload = document.Autopilot.HardwareHashUpload;
        if (!CanDiscoverHardwareHashGroupTags(document.Autopilot, hardwareHashUpload))
        {
            return WithRuntimeGroupTags(document, []);
        }

        try
        {
            string workspaceRootPath = ResolveWorkspaceRootPath(cacheRootPath);
            IReadOnlyList<string> groupTags = await _autopilotGroupTagDiscoveryService
                .DiscoverAsync(hardwareHashUpload, workspaceRootPath)
                .ConfigureAwait(false);
            _logger.LogInformation("Discovered {GroupTagCount} Autopilot group tag(s) at Deploy startup.", groupTags.Count);
            return WithRuntimeGroupTags(document, groupTags);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or FileNotFoundException or CryptographicException or HttpRequestException or JsonException)
        {
            _logger.LogWarning(ex, "Autopilot group tag discovery failed. Deploy will default to no group tag.");
            return WithRuntimeGroupTags(document, []);
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

    private sealed record HardwareLoadResult(HardwareProfile? Profile, string? ErrorMessage);
    private sealed record TargetDiskLoadResult(IReadOnlyList<TargetDiskInfo> Disks);
}
