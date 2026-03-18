using System.IO;
using Foundry.Deploy.Models;
using Foundry.Deploy.Models.Configuration;
using Foundry.Deploy.Services.Catalog;
using Foundry.Deploy.Services.Configuration;
using Foundry.Deploy.Services.Hardware;
using Foundry.Deploy.Services.Runtime;
using Microsoft.Extensions.Logging;

namespace Foundry.Deploy.Services.Startup;

public sealed class DeploymentStartupCoordinator : IDeploymentStartupCoordinator
{
    private const string WinPeTransientRuntimeRoot = @"X:\Foundry\Runtime";

    private readonly IExpertDeployConfigurationService _expertDeployConfigurationService;
    private readonly IHardwareProfileService _hardwareProfileService;
    private readonly IOfflineWindowsComputerNameService _offlineWindowsComputerNameService;
    private readonly ITargetDiskService _targetDiskService;
    private readonly IDeploymentCatalogLoadService _deploymentCatalogLoadService;
    private readonly ILogger<DeploymentStartupCoordinator> _logger;

    public DeploymentStartupCoordinator(
        IExpertDeployConfigurationService expertDeployConfigurationService,
        IHardwareProfileService hardwareProfileService,
        IOfflineWindowsComputerNameService offlineWindowsComputerNameService,
        ITargetDiskService targetDiskService,
        IDeploymentCatalogLoadService deploymentCatalogLoadService,
        ILogger<DeploymentStartupCoordinator> logger)
    {
        _expertDeployConfigurationService = expertDeployConfigurationService;
        _hardwareProfileService = hardwareProfileService;
        _offlineWindowsComputerNameService = offlineWindowsComputerNameService;
        _targetDiskService = targetDiskService;
        _deploymentCatalogLoadService = deploymentCatalogLoadService;
        _logger = logger;
    }

    public async Task<DeploymentStartupSnapshot> InitializeAsync(DeploymentStartupRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        ExpertDeployConfigurationLoadResult expertConfigLoadResult = _expertDeployConfigurationService.LoadOptional();
        string cacheRootPath = ResolveCacheRootPath(request.RuntimeContext, request.IsDebugSafeMode);
        string? startupStatusMessage = request.IsDebugSafeMode
            ? "Debug Safe Mode enabled: deployment actions are simulated."
            : null;

        Task<string> computerNameTask = ResolveComputerNameAsync(request.FallbackComputerName);
        Task<HardwareLoadResult> hardwareTask = LoadHardwareAsync();
        Task<TargetDiskLoadResult> targetDisksTask = LoadTargetDisksAsync();
        Task<DeploymentCatalogSnapshot> catalogTask = _deploymentCatalogLoadService.LoadAsync();

        await Task.WhenAll(computerNameTask, hardwareTask, targetDisksTask, catalogTask).ConfigureAwait(false);

        return new DeploymentStartupSnapshot
        {
            CacheRootPath = cacheRootPath,
            StartupStatusMessage = startupStatusMessage,
            ExpertConfigurationDocument = expertConfigLoadResult.Document,
            EffectiveComputerName = computerNameTask.Result,
            DetectedHardware = hardwareTask.Result.Profile,
            HardwareDetectionFailureMessage = hardwareTask.Result.ErrorMessage,
            TargetDisks = targetDisksTask.Result.Disks,
            TargetDiskStatusMessage = targetDisksTask.Result.StatusMessage,
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

    private async Task<TargetDiskLoadResult> LoadTargetDisksAsync()
    {
        try
        {
            IReadOnlyList<TargetDiskInfo> disks = await _targetDiskService.GetDisksAsync().ConfigureAwait(false);
            return new TargetDiskLoadResult(disks, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Target disk discovery failed during startup.");
            return new TargetDiskLoadResult([], $"Target disk discovery failed: {ex.Message}");
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

    private sealed record HardwareLoadResult(HardwareProfile? Profile, string? ErrorMessage);
    private sealed record TargetDiskLoadResult(IReadOnlyList<TargetDiskInfo> Disks, string? StatusMessage);
}
