using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Foundry.Deploy.Models;
using Foundry.Deploy.Models.Configuration;
using Foundry.Deploy.Services.Hardware;
using Foundry.Deploy.Services.Localization;
using Foundry.Deploy.Services.Runtime;
using Foundry.Deploy.Services.System;
using Foundry.Deploy.Validation;
using Microsoft.Extensions.Logging;

namespace Foundry.Deploy.ViewModels;

public sealed partial class DeploymentPreparationViewModel : LocalizedViewModelBase
{
    private const string DebugAutopilotProfileFolderName = "DebugAutopilotProfile";
    private const string DebugAutopilotProfileDisplayName = "Debug Autopilot Profile";
    private const string DebugAutopilotGroupTag = "Debug";

    private readonly ITargetDiskService _targetDiskService;
    private readonly IHardwareProfileService _hardwareProfileService;
    private readonly IOfflineWindowsComputerNameService _offlineWindowsComputerNameService;
    private readonly ILogger _logger;
    private readonly bool _isDebugSafeMode;
    private HardwareProfile? _detectedHardware;
    private DeployMachineNamingSettings _machineNamingConfiguration = new();
    private string _lockedComputerNamePrefix = string.Empty;
    private string _detectedHardwareSummaryRaw = "Detecting hardware...";
    private bool _isApplyingManagedComputerName;
    private bool _isUpdatingFirmwareOptionSelection;
    private bool _hasUserSelectedFirmwareOption;
    private bool _firmwareUpdatesPreference = true;

    public DeploymentPreparationViewModel(
        ITargetDiskService targetDiskService,
        IHardwareProfileService hardwareProfileService,
        IOfflineWindowsComputerNameService offlineWindowsComputerNameService,
        ILocalizationService localizationService,
        ILogger logger,
        bool isDebugSafeMode)
        : base(localizationService)
    {
        _targetDiskService = targetDiskService;
        _hardwareProfileService = hardwareProfileService;
        _offlineWindowsComputerNameService = offlineWindowsComputerNameService;
        _logger = logger;
        _isDebugSafeMode = isDebugSafeMode;
        LocalizationService.LanguageChanged += OnLocalizationLanguageChanged;
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
    private bool isAutopilotEnabled;

    [ObservableProperty]
    private AutopilotProvisioningMode autopilotProvisioningMode = AutopilotProvisioningMode.JsonProfile;

    [ObservableProperty]
    private DeployAutopilotHardwareHashUploadSettings autopilotHardwareHashUpload = new();

    [ObservableProperty]
    private bool useDefaultHardwareHashGroupTag = true;

    [ObservableProperty]
    private bool useCustomHardwareHashGroupTag;

    [ObservableProperty]
    private string customHardwareHashGroupTag = string.Empty;

    [ObservableProperty]
    private AutopilotProfileCatalogItem? selectedAutopilotProfile;

    [ObservableProperty]
    private string detectedHardwareSummary = LocalizationText.GetString("Preparation.DetectingHardware");

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefreshTargetDisksCommand))]
    private bool isTargetDiskLoading;

    public ObservableCollection<TargetDiskInfo> TargetDisks { get; } = [];
    public ObservableCollection<AutopilotProfileCatalogItem> AutopilotProfiles { get; } = [];

    public bool IsFirmwareUpdatesOptionEnabled => _detectedHardware?.IsVirtualMachine != true;
    public bool HasAutopilotProfiles => AutopilotProfiles.Count > 0;
    public bool IsAutopilotSectionVisible => IsAutopilotEnabled || HasAutopilotProfiles;
    public bool IsJsonProfileMode => AutopilotProvisioningMode == AutopilotProvisioningMode.JsonProfile;
    public bool IsHardwareHashUploadMode => AutopilotProvisioningMode == AutopilotProvisioningMode.HardwareHashUpload;
    public bool IsAutopilotDisabledSummaryVisible => IsAutopilotSectionVisible && !IsAutopilotEnabled;
    public bool IsJsonProfileControlsVisible => IsAutopilotEnabled && IsJsonProfileMode;
    public bool IsHardwareHashUploadControlsVisible => IsAutopilotEnabled && IsHardwareHashUploadMode;
    public bool IsAutopilotProfileSelectionEnabled => IsJsonProfileControlsVisible && HasAutopilotProfiles;
    public bool HasHardwareHashUploadMetadata =>
        !string.IsNullOrWhiteSpace(AutopilotHardwareHashUpload.TenantId) &&
        !string.IsNullOrWhiteSpace(AutopilotHardwareHashUpload.ClientId) &&
        !string.IsNullOrWhiteSpace(AutopilotHardwareHashUpload.ActiveCertificateKeyId) &&
        !string.IsNullOrWhiteSpace(AutopilotHardwareHashUpload.ActiveCertificateThumbprint) &&
        AutopilotHardwareHashUpload.ActiveCertificateExpiresOnUtc is not null;
    public bool IsHardwareHashCertificateExpired =>
        AutopilotHardwareHashUpload.ActiveCertificateExpiresOnUtc is DateTimeOffset expiresOn &&
        expiresOn <= DateTimeOffset.UtcNow;
    public bool IsHardwareHashCertificateUsable => HasHardwareHashUploadMetadata && !IsHardwareHashCertificateExpired;
    public bool IsHardwareHashGroupTagControlsVisible => IsHardwareHashUploadControlsVisible && IsHardwareHashCertificateUsable;
    public bool IsHardwareHashMissingMetadataWarningVisible => IsHardwareHashUploadControlsVisible && !HasHardwareHashUploadMetadata;
    public bool HasHardwareHashDefaultGroupTag => !string.IsNullOrWhiteSpace(AutopilotHardwareHashUpload.DefaultGroupTag);
    public bool IsDefaultHardwareHashGroupTagOptionVisible => IsHardwareHashGroupTagControlsVisible && HasHardwareHashDefaultGroupTag;
    public bool IsHardwareHashCustomGroupTagTextBoxVisible => IsHardwareHashGroupTagControlsVisible && (UseCustomHardwareHashGroupTag || !HasHardwareHashDefaultGroupTag);
    public bool IsHardwareHashCustomGroupTagEnabled => IsHardwareHashGroupTagControlsVisible && (UseCustomHardwareHashGroupTag || !HasHardwareHashDefaultGroupTag);
    public string TargetDiskSelectionHint => !string.IsNullOrWhiteSpace(SelectedTargetDisk?.SelectionWarning)
        ? SelectedTargetDisk.SelectionWarning
        : GetString("Preparation.TargetDiskHint");
    public string AutopilotProfileHint =>
        !IsJsonProfileMode
            ? string.Empty
            : HasAutopilotProfiles
            ? string.Empty
            : IsAutopilotEnabled
                ? GetString("Preparation.AutopilotProfilesMissing")
                : string.Empty;
    public bool HasAutopilotProfileHint => !string.IsNullOrWhiteSpace(AutopilotProfileHint);
    public string AutopilotModeText => IsHardwareHashUploadMode
        ? GetString("Preparation.AutopilotModeHardwareHashUpload")
        : GetString("Preparation.AutopilotModeJsonProfile");
    public string AutopilotDisabledSummaryText => Format("Preparation.AutopilotConfiguredModeFormat", AutopilotModeText);
    public string AutopilotHardwareHashUploadStatusText
    {
        get
        {
            if (IsHardwareHashCertificateUsable)
            {
                return GetString("Preparation.AutopilotHardwareHashReadyStatus");
            }

            return IsHardwareHashCertificateExpired
                ? GetString("Preparation.AutopilotHardwareHashExpiredStatus")
                : GetString("Preparation.AutopilotHardwareHashNotReadyStatus");
        }
    }

    public string AutopilotHardwareHashUploadMessage
    {
        get
        {
            if (IsHardwareHashCertificateUsable)
            {
                return GetString("Preparation.AutopilotHardwareHashReadyMessage");
            }

            if (IsHardwareHashCertificateExpired)
            {
                return GetString("Preparation.AutopilotHardwareHashExpiredMessage");
            }

            return GetString("Preparation.AutopilotHardwareHashMissingMetadataMessage");
        }
    }

    public string AutopilotHardwareHashDefaultGroupTagText => string.IsNullOrWhiteSpace(AutopilotHardwareHashUpload.DefaultGroupTag)
        ? GetString("Common.None")
        : AutopilotHardwareHashUpload.DefaultGroupTag!;
    public string UseDefaultHardwareHashGroupTagText => Format(
        "Preparation.AutopilotHardwareHashUseDefaultGroupTagFormat",
        AutopilotHardwareHashDefaultGroupTagText);
    public string EffectiveHardwareHashGroupTagText => string.IsNullOrWhiteSpace(ResolveEffectiveHardwareHashGroupTag())
        ? GetString("Common.None")
        : ResolveEffectiveHardwareHashGroupTag()!;

    public bool HasTargetComputerNameValidationError => !string.IsNullOrWhiteSpace(TargetComputerNameValidationMessage);

    public HardwareProfile? DetectedHardware => _detectedHardware;

    /// <summary>
    /// Builds the hardware hash upload settings to carry into a deployment launch request.
    /// </summary>
    /// <returns>The configured hardware hash upload metadata with the user-selected group tag override applied.</returns>
    public DeployAutopilotHardwareHashUploadSettings CreateAutopilotHardwareHashUploadForLaunch()
    {
        if (!IsAutopilotEnabled || !IsHardwareHashUploadMode)
        {
            return new DeployAutopilotHardwareHashUploadSettings();
        }

        return AutopilotHardwareHashUpload with
        {
            DefaultGroupTag = ResolveEffectiveHardwareHashGroupTag()
        };
    }

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
            string statusMessage = ApplyTargetDisks(disks);
            PublishStatus(statusMessage);
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

    public void ApplyAutopilotConfiguration(
        DeployAutopilotSettings settings,
        IReadOnlyList<AutopilotProfileCatalogItem> profiles)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(profiles);

        AutopilotProfiles.Clear();
        foreach (AutopilotProfileCatalogItem profile in profiles)
        {
            AutopilotProfiles.Add(profile);
        }

        OnPropertyChanged(nameof(HasAutopilotProfiles));
        OnPropertyChanged(nameof(IsAutopilotSectionVisible));
        OnPropertyChanged(nameof(IsAutopilotProfileSelectionEnabled));
        OnPropertyChanged(nameof(AutopilotProfileHint));
        OnPropertyChanged(nameof(HasAutopilotProfileHint));

        AutopilotProvisioningMode = settings.ProvisioningMode;
        AutopilotHardwareHashUpload = settings.HardwareHashUpload ?? new DeployAutopilotHardwareHashUploadSettings();
        UseDefaultHardwareHashGroupTag = true;
        UseCustomHardwareHashGroupTag = false;
        CustomHardwareHashGroupTag = string.Empty;
        SelectedAutopilotProfile = settings.IsEnabled && settings.ProvisioningMode == AutopilotProvisioningMode.JsonProfile
            ? ResolveDefaultAutopilotProfile(settings.DefaultProfileFolderName)
            : null;
        IsAutopilotEnabled = settings.IsEnabled;

        RaiseStateChanged();
    }

    public void SetDetectedHardware(HardwareProfile? profile)
    {
        _detectedHardware = profile;

        if (profile is null)
        {
            _detectedHardwareSummaryRaw = "Hardware detection failed.";
            DetectedHardwareSummary = DeploymentUiTextLocalizer.LocalizeMessage(_detectedHardwareSummaryRaw);
            OnPropertyChanged(nameof(IsFirmwareUpdatesOptionEnabled));
            RaiseStateChanged();
            return;
        }

        SyncFirmwareOptionFromHardware(profile);
        _detectedHardwareSummaryRaw = Format(
            "Preparation.HardwareSummaryFormat",
            profile.DisplayLabel,
            profile.IsTpmPresent ? GetString("Common.Yes") : GetString("Common.No"),
            profile.IsOnBattery ? GetString("Preparation.PowerBattery") : GetString("Preparation.PowerAc"),
            profile.SystemFirmwareHardwareId.Length > 0 ? GetString("Common.Detected") : GetString("Common.Unavailable"));
        DetectedHardwareSummary = _detectedHardwareSummaryRaw;
        OnPropertyChanged(nameof(IsFirmwareUpdatesOptionEnabled));
        RaiseStateChanged();
    }

    public void SetHardwareDetectionFailure(string message)
    {
        _detectedHardwareSummaryRaw = message;
        DetectedHardwareSummary = DeploymentUiTextLocalizer.LocalizeMessage(message);
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

    public string ApplyTargetDisks(IReadOnlyList<TargetDiskInfo> disks)
    {
        ArgumentNullException.ThrowIfNull(disks);

        TargetDisks.Clear();
        foreach (TargetDiskInfo disk in disks)
        {
            TargetDisks.Add(disk);
        }

        if (_isDebugSafeMode && !TargetDisks.Any(item => item.IsSelectable))
        {
            TargetDisks.Insert(0, TargetDiskInfoFactory.CreateDebugVirtualDisk());
        }

        if (TargetDisks.Count == 0)
        {
            SelectedTargetDisk = null;
            RaiseStateChanged();
            return "No disks detected.";
        }

        TargetDiskInfo? currentSelection = SelectedTargetDisk is null
            ? null
            : TargetDisks.FirstOrDefault(item => item.DiskNumber == SelectedTargetDisk.DiskNumber);

        SelectedTargetDisk = currentSelection
            ?? TargetDisks.FirstOrDefault(item => item.IsSelectable)
            ?? (_isDebugSafeMode ? TargetDisks.FirstOrDefault(item => item.DiskNumber == TargetDiskInfoFactory.CreateDebugVirtualDisk().DiskNumber) : null)
            ?? TargetDisks.FirstOrDefault();

        RaiseStateChanged();
        return $"Target disks loaded: {TargetDisks.Count} detected.";
    }

    partial void OnTargetComputerNameChanged(string value)
    {
        if (_isApplyingManagedComputerName)
        {
            RaiseStateChanged();
            return;
        }

        TargetComputerNameValidationMessage = ResolveComputerNameValidationMessage(value);
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

        TargetComputerNameValidationMessage = ResolveComputerNameValidationMessage(normalized);
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

    partial void OnIsAutopilotEnabledChanged(bool value)
    {
        OnPropertyChanged(nameof(IsAutopilotSectionVisible));
        OnPropertyChanged(nameof(IsAutopilotDisabledSummaryVisible));
        OnPropertyChanged(nameof(IsJsonProfileControlsVisible));
        OnPropertyChanged(nameof(IsHardwareHashUploadControlsVisible));
        OnPropertyChanged(nameof(IsAutopilotProfileSelectionEnabled));
        OnPropertyChanged(nameof(AutopilotProfileHint));
        OnPropertyChanged(nameof(HasAutopilotProfileHint));
        RaiseHardwareHashPropertiesChanged();
        RaiseStateChanged();
    }

    /// <summary>
    /// Applies an in-memory Autopilot mode override for debug safe mode without changing the persisted deployment configuration.
    /// </summary>
    /// <param name="mode">Debug Autopilot mode to apply.</param>
    public void ApplyDebugAutopilotMode(DebugAutopilotMode mode)
    {
        switch (mode)
        {
            case DebugAutopilotMode.None:
                IsAutopilotEnabled = false;
                AutopilotProvisioningMode = AutopilotProvisioningMode.JsonProfile;
                SelectedAutopilotProfile = null;
                break;
            case DebugAutopilotMode.JsonProfile:
                EnsureDebugAutopilotProfile();
                UseDefaultHardwareHashGroupTag = true;
                UseCustomHardwareHashGroupTag = false;
                CustomHardwareHashGroupTag = string.Empty;
                AutopilotProvisioningMode = AutopilotProvisioningMode.JsonProfile;
                SelectedAutopilotProfile = AutopilotProfiles.First(profile =>
                    profile.FolderName.Equals(DebugAutopilotProfileFolderName, StringComparison.OrdinalIgnoreCase));
                IsAutopilotEnabled = true;
                break;
            case DebugAutopilotMode.HardwareHashUploadValidCertificate:
                ApplyDebugHardwareHashUpload(
                    certificateExpiresOnUtc: DateTimeOffset.UtcNow.AddMonths(1),
                    defaultGroupTag: DebugAutopilotGroupTag,
                    includeCompleteCertificateMetadata: true);
                break;
            case DebugAutopilotMode.HardwareHashUploadExpiredCertificate:
                ApplyDebugHardwareHashUpload(
                    certificateExpiresOnUtc: DateTimeOffset.UtcNow.AddDays(-1),
                    defaultGroupTag: DebugAutopilotGroupTag,
                    includeCompleteCertificateMetadata: true);
                break;
            case DebugAutopilotMode.HardwareHashUploadMissingCertificateMetadata:
                ApplyDebugHardwareHashUpload(
                    certificateExpiresOnUtc: DateTimeOffset.UtcNow.AddMonths(1),
                    defaultGroupTag: DebugAutopilotGroupTag,
                    includeCompleteCertificateMetadata: false);
                break;
            case DebugAutopilotMode.HardwareHashUploadNoDefaultGroupTag:
                ApplyDebugHardwareHashUpload(
                    certificateExpiresOnUtc: DateTimeOffset.UtcNow.AddMonths(1),
                    defaultGroupTag: null,
                    includeCompleteCertificateMetadata: true);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unsupported debug Autopilot mode.");
        }

        RaiseStateChanged();
    }

    private void ApplyDebugHardwareHashUpload(DateTimeOffset certificateExpiresOnUtc, string? defaultGroupTag, bool includeCompleteCertificateMetadata)
    {
        AutopilotProvisioningMode = AutopilotProvisioningMode.HardwareHashUpload;
        AutopilotHardwareHashUpload = new DeployAutopilotHardwareHashUploadSettings
        {
            TenantId = "debug-tenant-id",
            ClientId = "debug-client-id",
            ActiveCertificateKeyId = includeCompleteCertificateMetadata ? "debug-certificate-key-id" : null,
            ActiveCertificateThumbprint = includeCompleteCertificateMetadata ? "DEBUGTHUMBPRINT" : null,
            ActiveCertificateExpiresOnUtc = includeCompleteCertificateMetadata ? certificateExpiresOnUtc : null,
            DefaultGroupTag = defaultGroupTag
        };
        UseDefaultHardwareHashGroupTag = true;
        UseCustomHardwareHashGroupTag = false;
        CustomHardwareHashGroupTag = string.Empty;
        SelectedAutopilotProfile = null;
        IsAutopilotEnabled = true;
    }

    partial void OnAutopilotProvisioningModeChanged(AutopilotProvisioningMode value)
    {
        OnPropertyChanged(nameof(IsJsonProfileMode));
        OnPropertyChanged(nameof(IsHardwareHashUploadMode));
        OnPropertyChanged(nameof(IsJsonProfileControlsVisible));
        OnPropertyChanged(nameof(IsHardwareHashUploadControlsVisible));
        OnPropertyChanged(nameof(IsAutopilotProfileSelectionEnabled));
        OnPropertyChanged(nameof(IsAutopilotDisabledSummaryVisible));
        OnPropertyChanged(nameof(AutopilotProfileHint));
        OnPropertyChanged(nameof(HasAutopilotProfileHint));
        OnPropertyChanged(nameof(AutopilotModeText));
        OnPropertyChanged(nameof(AutopilotDisabledSummaryText));
        RaiseHardwareHashPropertiesChanged();
        RaiseStateChanged();
    }

    partial void OnAutopilotHardwareHashUploadChanged(DeployAutopilotHardwareHashUploadSettings value)
    {
        RaiseHardwareHashPropertiesChanged();
        RaiseStateChanged();
    }

    partial void OnUseDefaultHardwareHashGroupTagChanged(bool value)
    {
        if (value && UseCustomHardwareHashGroupTag)
        {
            UseCustomHardwareHashGroupTag = false;
        }

        OnPropertyChanged(nameof(IsHardwareHashCustomGroupTagEnabled));
        OnPropertyChanged(nameof(IsHardwareHashCustomGroupTagTextBoxVisible));
        OnPropertyChanged(nameof(EffectiveHardwareHashGroupTagText));
        RaiseStateChanged();
    }

    partial void OnUseCustomHardwareHashGroupTagChanged(bool value)
    {
        if (value && UseDefaultHardwareHashGroupTag)
        {
            UseDefaultHardwareHashGroupTag = false;
        }
        else if (!value && !UseDefaultHardwareHashGroupTag)
        {
            UseDefaultHardwareHashGroupTag = true;
        }

        OnPropertyChanged(nameof(IsHardwareHashCustomGroupTagEnabled));
        OnPropertyChanged(nameof(IsHardwareHashCustomGroupTagTextBoxVisible));
        OnPropertyChanged(nameof(EffectiveHardwareHashGroupTagText));
        RaiseStateChanged();
    }

    partial void OnCustomHardwareHashGroupTagChanged(string value)
    {
        OnPropertyChanged(nameof(EffectiveHardwareHashGroupTagText));
        RaiseStateChanged();
    }

    partial void OnSelectedAutopilotProfileChanged(AutopilotProfileCatalogItem? value)
    {
        OnPropertyChanged(nameof(IsAutopilotProfileSelectionEnabled));
        RaiseStateChanged();
    }

    partial void OnSelectedTargetDiskChanged(TargetDiskInfo? value)
    {
        OnPropertyChanged(nameof(TargetDiskSelectionHint));
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
            TargetComputerNameValidationMessage = ResolveComputerNameValidationMessage(value);
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

    private AutopilotProfileCatalogItem? ResolveDefaultAutopilotProfile(string? defaultProfileFolderName)
    {
        if (!string.IsNullOrWhiteSpace(defaultProfileFolderName))
        {
            AutopilotProfileCatalogItem? matchingProfile = AutopilotProfiles.FirstOrDefault(profile =>
                profile.FolderName.Equals(defaultProfileFolderName, StringComparison.OrdinalIgnoreCase));
            if (matchingProfile is not null)
            {
                return matchingProfile;
            }
        }

        return null;
    }

    private void EnsureDebugAutopilotProfile()
    {
        if (AutopilotProfiles.Any(profile =>
                profile.FolderName.Equals(DebugAutopilotProfileFolderName, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        AutopilotProfiles.Add(new AutopilotProfileCatalogItem
        {
            FolderName = DebugAutopilotProfileFolderName,
            DisplayName = DebugAutopilotProfileDisplayName,
            ConfigurationFilePath = @"X:\Foundry\Debug\Autopilot\AutopilotConfigurationFile.json"
        });
        OnPropertyChanged(nameof(HasAutopilotProfiles));
        OnPropertyChanged(nameof(IsAutopilotSectionVisible));
        OnPropertyChanged(nameof(IsAutopilotProfileSelectionEnabled));
        OnPropertyChanged(nameof(AutopilotProfileHint));
        OnPropertyChanged(nameof(HasAutopilotProfileHint));
    }

    private void RaiseStateChanged()
    {
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void PublishStatus(string message)
    {
        StatusMessageGenerated?.Invoke(message);
    }

    public override void Dispose()
    {
        LocalizationService.LanguageChanged -= OnLocalizationLanguageChanged;
        base.Dispose();
    }

    private void OnLocalizationLanguageChanged(object? sender, EventArgs e)
    {
        RunOnUiThread(() =>
        {
            if (_detectedHardware is not null)
            {
                SetDetectedHardware(_detectedHardware);
            }
            else
            {
                DetectedHardwareSummary = DeploymentUiTextLocalizer.LocalizeMessage(_detectedHardwareSummaryRaw);
            }

            TargetComputerNameValidationMessage = ResolveComputerNameValidationMessage(TargetComputerName);
            OnPropertyChanged(nameof(AutopilotProfileHint));
            OnPropertyChanged(nameof(HasAutopilotProfileHint));
            OnPropertyChanged(nameof(AutopilotModeText));
            OnPropertyChanged(nameof(AutopilotDisabledSummaryText));
            RaiseHardwareHashPropertiesChanged();
            OnPropertyChanged(nameof(TargetDiskSelectionHint));
            OnPropertyChanged(nameof(TargetDisks));
            OnPropertyChanged(nameof(SelectedTargetDisk));
        });
    }

    private string ResolveComputerNameValidationMessage(string? value)
    {
        return ComputerNameRules.IsValid(value)
            ? string.Empty
            : GetString("Preparation.ComputerNameValidationMessage");
    }

    private string GetString(string key)
    {
        return Strings[key];
    }

    private string Format(string key, params object[] args)
    {
        return string.Format(LocalizationService.CurrentCulture, GetString(key), args);
    }

    private string? ResolveEffectiveHardwareHashGroupTag()
    {
        string? groupTag = UseCustomHardwareHashGroupTag || !HasHardwareHashDefaultGroupTag
            ? CustomHardwareHashGroupTag
            : AutopilotHardwareHashUpload.DefaultGroupTag;

        return string.IsNullOrWhiteSpace(groupTag)
            ? null
            : groupTag.Trim();
    }

    private void RaiseHardwareHashPropertiesChanged()
    {
        OnPropertyChanged(nameof(HasHardwareHashUploadMetadata));
        OnPropertyChanged(nameof(IsHardwareHashCertificateExpired));
        OnPropertyChanged(nameof(IsHardwareHashCertificateUsable));
        OnPropertyChanged(nameof(IsHardwareHashGroupTagControlsVisible));
        OnPropertyChanged(nameof(IsHardwareHashMissingMetadataWarningVisible));
        OnPropertyChanged(nameof(HasHardwareHashDefaultGroupTag));
        OnPropertyChanged(nameof(IsDefaultHardwareHashGroupTagOptionVisible));
        OnPropertyChanged(nameof(IsHardwareHashCustomGroupTagTextBoxVisible));
        OnPropertyChanged(nameof(IsHardwareHashCustomGroupTagEnabled));
        OnPropertyChanged(nameof(AutopilotHardwareHashUploadStatusText));
        OnPropertyChanged(nameof(AutopilotHardwareHashUploadMessage));
        OnPropertyChanged(nameof(AutopilotHardwareHashDefaultGroupTagText));
        OnPropertyChanged(nameof(UseDefaultHardwareHashGroupTagText));
        OnPropertyChanged(nameof(EffectiveHardwareHashGroupTagText));
    }

    private bool CanRefreshTargetDisks()
    {
        return !IsTargetDiskLoading;
    }

}
