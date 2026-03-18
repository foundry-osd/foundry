using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Foundry.Deploy.Models;
using Foundry.Deploy.Models.Configuration;
using Foundry.Deploy.Validation;

namespace Foundry.Deploy.ViewModels;

public sealed partial class DeploymentPreparationViewModel : ObservableObject
{
    private HardwareProfile? _detectedHardware;
    private DeployMachineNamingSettings _machineNamingConfiguration = new();
    private string _lockedComputerNamePrefix = string.Empty;
    private bool _isApplyingManagedComputerName;
    private bool _isUpdatingFirmwareOptionSelection;
    private bool _hasUserSelectedFirmwareOption;
    private bool _firmwareUpdatesPreference = true;

    public event EventHandler? StateChanged;

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

    public ObservableCollection<TargetDiskInfo> TargetDisks { get; } = [];

    public bool IsFirmwareUpdatesOptionEnabled => _detectedHardware?.IsVirtualMachine != true;

    public bool HasTargetComputerNameValidationError => !string.IsNullOrWhiteSpace(TargetComputerNameValidationMessage);

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
}
