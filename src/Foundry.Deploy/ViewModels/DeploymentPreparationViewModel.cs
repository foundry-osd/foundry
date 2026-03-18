using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Foundry.Deploy.Models;
using Foundry.Deploy.Models.Configuration;
using Foundry.Deploy.Services.Hardware;
using Foundry.Deploy.Services.System;
using Foundry.Deploy.Validation;
using Microsoft.Extensions.Logging;

namespace Foundry.Deploy.ViewModels;

public sealed partial class DeploymentPreparationViewModel : ObservableObject
{
    private readonly ITargetDiskService _targetDiskService;
    private readonly IHardwareProfileService _hardwareProfileService;
    private readonly IOfflineWindowsComputerNameService _offlineWindowsComputerNameService;
    private readonly ILogger _logger;
    private readonly bool _isDebugSafeMode;
    private HardwareProfile? _detectedHardware;
    private DeployMachineNamingSettings _machineNamingConfiguration = new();
    private string _lockedComputerNamePrefix = string.Empty;
    private bool _isApplyingManagedComputerName;
    private bool _isUpdatingFirmwareOptionSelection;
    private bool _hasUserSelectedFirmwareOption;
    private bool _firmwareUpdatesPreference = true;

    public DeploymentPreparationViewModel(
        ITargetDiskService targetDiskService,
        IHardwareProfileService hardwareProfileService,
        IOfflineWindowsComputerNameService offlineWindowsComputerNameService,
        ILogger logger,
        bool isDebugSafeMode)
    {
        _targetDiskService = targetDiskService;
        _hardwareProfileService = hardwareProfileService;
        _offlineWindowsComputerNameService = offlineWindowsComputerNameService;
        _logger = logger;
        _isDebugSafeMode = isDebugSafeMode;
    }

    public event EventHandler? StateChanged;
    public event Action<string>? StatusMessageGenerated;

    [ObservableProperty]
    private string targetComputerName = string.Empty;

    [ObservableProperty]
    private bool isTargetComputerNameReadOnly;

    [ObservableProperty]
    private string targetComputerNameValidationMessage = string.Empty;

    [ObservableProperty]
    private TargetDiskInfo? selectedTargetDisk;

    [ObservableProperty]
    private string cacheRootPath = string.Empty;

    [ObservableProperty]
    private bool applyFirmwareUpdates = true;

    [ObservableProperty]
    private string detectedHardwareSummary = "Detecting hardware...";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefreshTargetDisksCommand))]
    private bool isTargetDiskLoading;

    public ObservableCollection<TargetDiskInfo> TargetDisks { get; } = [];

    public bool IsFirmwareUpdatesOptionEnabled => _detectedHardware?.IsVirtualMachine != true;

    public bool HasTargetComputerNameValidationError => !string.IsNullOrWhiteSpace(TargetComputerNameValidationMessage);

    public HardwareProfile? DetectedHardware => _detectedHardware;

    [RelayCommand(CanExecute = nameof(CanRefreshTargetDisks))]
    private async Task RefreshTargetDisksAsync()
    {
        _logger.LogInformation("Refreshing target disk list.");
        if (IsTargetDiskLoading)
        {
            return;
        }

        IsTargetDiskLoading = true;
        PublishStatus("Loading target disks...");

        try
        {
            IReadOnlyList<TargetDiskInfo> disks = await _targetDiskService.GetDisksAsync();

            TargetDisks.Clear();
            foreach (TargetDiskInfo disk in disks)
            {
                TargetDisks.Add(disk);
            }

            if (_isDebugSafeMode && !TargetDisks.Any(item => item.IsSelectable))
            {
                TargetDisks.Insert(0, CreateDebugVirtualDisk());
            }

            if (TargetDisks.Count == 0)
            {
                SelectedTargetDisk = null;
                PublishStatus("No disks detected.");
                return;
            }

            TargetDiskInfo? currentSelection = SelectedTargetDisk is null
                ? null
                : TargetDisks.FirstOrDefault(item => item.DiskNumber == SelectedTargetDisk.DiskNumber);

            SelectedTargetDisk = currentSelection
                ?? TargetDisks.FirstOrDefault(item => item.IsSelectable)
                ?? (_isDebugSafeMode ? TargetDisks.FirstOrDefault(item => item.DiskNumber == CreateDebugVirtualDisk().DiskNumber) : null)
                ?? TargetDisks.FirstOrDefault();

            PublishStatus($"Target disks loaded: {TargetDisks.Count} detected.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Target disk discovery failed.");
            PublishStatus($"Target disk discovery failed: {ex.Message}");
        }
        finally
        {
            IsTargetDiskLoading = false;
            RaiseStateChanged();
        }
    }

    public async Task LoadHardwareProfileAsync()
    {
        try
        {
            HardwareProfile profile = await _hardwareProfileService.GetCurrentAsync();
            SetDetectedHardware(profile);
            _logger.LogInformation("Hardware profile loaded in preparation view model. DisplayLabel={DisplayLabel}", profile.DisplayLabel);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Hardware profile loading failed in preparation view model.");
            SetHardwareDetectionFailure($"Hardware detection failed: {ex.Message}");
        }
    }

    public async Task LoadOfflineComputerNameAsync(string fallbackName)
    {
        string? resolvedName = null;
        try
        {
            resolvedName = await _offlineWindowsComputerNameService.TryGetOfflineComputerNameAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load offline Windows computer name.");
        }

        string effectiveName = !string.IsNullOrWhiteSpace(resolvedName)
            ? resolvedName
            : fallbackName;
        ApplyOfflineComputerName(effectiveName);
    }

    public void ApplyMachineNamingConfiguration(DeployMachineNamingSettings settings, string seed)
    {
        _machineNamingConfiguration = settings ?? new DeployMachineNamingSettings();
        _lockedComputerNamePrefix = ComputerNameRules.Normalize(_machineNamingConfiguration.Prefix);
        IsTargetComputerNameReadOnly = _machineNamingConfiguration.IsEnabled && !_machineNamingConfiguration.AllowManualSuffixEdit;

        if (_machineNamingConfiguration.IsEnabled)
        {
            string effectiveSeed = string.IsNullOrWhiteSpace(TargetComputerName)
                ? seed
                : TargetComputerName;
            ApplyManagedComputerNameValue(BuildConfiguredComputerName(effectiveSeed));
        }

        RaiseStateChanged();
    }

    public void SetDetectedHardware(HardwareProfile? profile)
    {
        _detectedHardware = profile;

        if (profile is null)
        {
            DetectedHardwareSummary = "Hardware detection failed.";
            OnPropertyChanged(nameof(IsFirmwareUpdatesOptionEnabled));
            RaiseStateChanged();
            return;
        }

        SyncFirmwareOptionFromHardware(profile);
        DetectedHardwareSummary =
            $"{profile.DisplayLabel} | TPM: {(profile.IsTpmPresent ? "Yes" : "No")} | Autopilot: {(profile.IsAutopilotCapable ? "Capable" : "Needs checks")} | Power: {(profile.IsOnBattery ? "Battery" : "AC")} | Firmware: {(profile.SystemFirmwareHardwareId.Length > 0 ? "Detected" : "Unavailable")}";
        OnPropertyChanged(nameof(IsFirmwareUpdatesOptionEnabled));
        RaiseStateChanged();
    }

    public void SetHardwareDetectionFailure(string message)
    {
        DetectedHardwareSummary = message;
        RaiseStateChanged();
    }

    public void ApplyOfflineComputerName(string effectiveName)
    {
        if (!string.IsNullOrEmpty(TargetComputerName))
        {
            return;
        }

        string configuredName = BuildConfiguredComputerName(effectiveName);
        ApplyManagedComputerNameValue(configuredName);
        RaiseStateChanged();
    }

    partial void OnTargetComputerNameChanged(string value)
    {
        if (_isApplyingManagedComputerName)
        {
            RaiseStateChanged();
            return;
        }

        TargetComputerNameValidationMessage = ComputerNameRules.GetValidationMessage(value);
        if (string.IsNullOrWhiteSpace(value))
        {
            RaiseStateChanged();
            return;
        }

        string normalized = NormalizeManagedComputerNameValue(value);
        if (!normalized.Equals(value, StringComparison.Ordinal))
        {
            ApplyManagedComputerNameValue(normalized);
            return;
        }

        TargetComputerNameValidationMessage = ComputerNameRules.GetValidationMessage(normalized);
        RaiseStateChanged();
    }

    partial void OnApplyFirmwareUpdatesChanged(bool value)
    {
        if (_isUpdatingFirmwareOptionSelection)
        {
            RaiseStateChanged();
            return;
        }

        _hasUserSelectedFirmwareOption = true;
        _firmwareUpdatesPreference = value;
        RaiseStateChanged();
    }

    partial void OnSelectedTargetDiskChanged(TargetDiskInfo? value)
    {
        RaiseStateChanged();
    }

    private string NormalizeManagedComputerNameValue(string? value)
    {
        string normalized = ComputerNameRules.Normalize(value);
        if (!_machineNamingConfiguration.IsEnabled)
        {
            return normalized;
        }

        if (string.IsNullOrWhiteSpace(_lockedComputerNamePrefix))
        {
            return normalized;
        }

        if (!_machineNamingConfiguration.AllowManualSuffixEdit)
        {
            return BuildConfiguredComputerName(normalized);
        }

        string suffix = normalized.StartsWith(_lockedComputerNamePrefix, StringComparison.OrdinalIgnoreCase)
            ? normalized[_lockedComputerNamePrefix.Length..]
            : normalized;

        return CombineComputerName(_lockedComputerNamePrefix, suffix);
    }

    private string BuildConfiguredComputerName(string seed)
    {
        string normalizedSeed = ComputerNameRules.Normalize(seed);
        if (!_machineNamingConfiguration.IsEnabled)
        {
            return normalizedSeed;
        }

        if (string.IsNullOrWhiteSpace(_lockedComputerNamePrefix))
        {
            return _machineNamingConfiguration.AutoGenerateName
                ? NormalizeAutoGeneratedComputerNameSuffix(normalizedSeed)
                : normalizedSeed;
        }

        if (_machineNamingConfiguration.AutoGenerateName)
        {
            string generatedSuffix = NormalizeAutoGeneratedComputerNameSuffix(
                ExtractComputerNameSuffix(normalizedSeed, _lockedComputerNamePrefix));
            return CombineComputerName(_lockedComputerNamePrefix, generatedSuffix);
        }

        string existingSuffix = ExtractComputerNameSuffix(TargetComputerName, _lockedComputerNamePrefix);
        return CombineComputerName(_lockedComputerNamePrefix, existingSuffix);
    }

    private void ApplyManagedComputerNameValue(string value)
    {
        _isApplyingManagedComputerName = true;
        try
        {
            TargetComputerName = value;
            TargetComputerNameValidationMessage = ComputerNameRules.GetValidationMessage(value);
        }
        finally
        {
            _isApplyingManagedComputerName = false;
        }

        RaiseStateChanged();
    }

    private void SyncFirmwareOptionFromHardware(HardwareProfile profile)
    {
        bool desiredValue = profile.IsVirtualMachine
            ? false
            : _hasUserSelectedFirmwareOption
                ? _firmwareUpdatesPreference
                : true;

        _isUpdatingFirmwareOptionSelection = true;
        try
        {
            ApplyFirmwareUpdates = desiredValue;
        }
        finally
        {
            _isUpdatingFirmwareOptionSelection = false;
        }
    }

    private static string CombineComputerName(string prefix, string suffix)
    {
        string normalizedPrefix = ComputerNameRules.Normalize(prefix);
        string normalizedSuffix = ComputerNameRules.Normalize(suffix);

        if (string.IsNullOrWhiteSpace(normalizedPrefix))
        {
            return normalizedSuffix;
        }

        int maxSuffixLength = Math.Max(0, ComputerNameRules.MaxLength - normalizedPrefix.Length);
        if (normalizedSuffix.Length > maxSuffixLength)
        {
            normalizedSuffix = normalizedSuffix[..maxSuffixLength];
        }

        return ComputerNameRules.Normalize($"{normalizedPrefix}{normalizedSuffix}");
    }

    private static string ExtractComputerNameSuffix(string? computerName, string prefix)
    {
        string normalizedPrefix = ComputerNameRules.Normalize(prefix);
        if (string.IsNullOrWhiteSpace(normalizedPrefix))
        {
            return ComputerNameRules.Normalize(computerName);
        }

        string normalizedComputerName = ComputerNameRules.Normalize(computerName);
        if (normalizedComputerName.StartsWith(normalizedPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return normalizedComputerName[normalizedPrefix.Length..];
        }

        return normalizedComputerName;
    }

    private static string NormalizeAutoGeneratedComputerNameSuffix(string? value)
    {
        string normalized = ComputerNameRules.Normalize(value);
        return string.IsNullOrWhiteSpace(normalized)
            ? ComputerNameRules.FallbackName
            : normalized;
    }

    private void RaiseStateChanged()
    {
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void PublishStatus(string message)
    {
        StatusMessageGenerated?.Invoke(message);
    }

    private bool CanRefreshTargetDisks()
    {
        return !IsTargetDiskLoading;
    }

    public static TargetDiskInfo CreateDebugVirtualDisk()
    {
        return new TargetDiskInfo
        {
            DiskNumber = 999,
            FriendlyName = "DEBUG VIRTUAL TARGET",
            SerialNumber = "DEBUG-ONLY",
            BusType = "Virtual",
            PartitionStyle = "GPT",
            SizeBytes = 128UL * 1024UL * 1024UL * 1024UL,
            IsSystem = false,
            IsBoot = false,
            IsReadOnly = false,
            IsOffline = false,
            IsRemovable = false,
            IsSelectable = true,
            SelectionWarning = string.Empty
        };
    }
}
