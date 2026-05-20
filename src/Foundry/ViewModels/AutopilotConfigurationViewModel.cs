using System.Collections.ObjectModel;
using System.Text.Json;
using Azure.Identity;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Autopilot;
using Foundry.Core.Services.Application;
using Foundry.Core.Services.Configuration;
using Foundry.Services.Autopilot;
using Foundry.Services.Configuration;
using Foundry.Services.Localization;
using Microsoft.UI.Xaml;
using Serilog;

namespace Foundry.ViewModels;

/// <summary>
/// Backs the Autopilot configuration page, including local imports, tenant downloads, selection, and persistence.
/// </summary>
public sealed partial class AutopilotConfigurationViewModel : ObservableObject, IDisposable
{
    private readonly IFoundryConfigurationStateService configurationStateService;
    private readonly IAutopilotProfileImportService autopilotProfileImportService;
    private readonly IAutopilotTenantProfileService autopilotTenantProfileService;
    private readonly IAutopilotTenantOnboardingService autopilotTenantOnboardingService;
    private readonly IAutopilotTenantDownloadDialogService tenantDownloadDialogService;
    private readonly IAutopilotProfileSelectionDialogService profileSelectionDialogService;
    private readonly IFilePickerService filePickerService;
    private readonly IDialogService dialogService;
    private readonly IApplicationLocalizationService localizationService;
    private readonly ILogger logger;
    private bool isApplyingState = true;
    private bool isSavingState;
    private AutopilotProvisioningMode provisioningMode = AutopilotProvisioningMode.JsonProfile;
    private AutopilotHardwareHashUploadSettings hardwareHashUploadSettings = new();
    private AutopilotTenantOnboardingStatus? tenantOnboardingStatus;

    public AutopilotConfigurationViewModel(
        IFoundryConfigurationStateService configurationStateService,
        IAutopilotProfileImportService autopilotProfileImportService,
        IAutopilotTenantProfileService autopilotTenantProfileService,
        IAutopilotTenantOnboardingService autopilotTenantOnboardingService,
        IAutopilotTenantDownloadDialogService tenantDownloadDialogService,
        IAutopilotProfileSelectionDialogService profileSelectionDialogService,
        IFilePickerService filePickerService,
        IDialogService dialogService,
        IApplicationLocalizationService localizationService,
        ILogger logger)
    {
        this.configurationStateService = configurationStateService;
        this.autopilotProfileImportService = autopilotProfileImportService;
        this.autopilotTenantProfileService = autopilotTenantProfileService;
        this.autopilotTenantOnboardingService = autopilotTenantOnboardingService;
        this.tenantDownloadDialogService = tenantDownloadDialogService;
        this.profileSelectionDialogService = profileSelectionDialogService;
        this.filePickerService = filePickerService;
        this.dialogService = dialogService;
        this.localizationService = localizationService;
        this.logger = logger.ForContext<AutopilotConfigurationViewModel>();

        RefreshLocalizedText();
        ApplyState(configurationStateService.Current.Autopilot);

        localizationService.LanguageChanged += OnLanguageChanged;
        configurationStateService.StateChanged += OnConfigurationStateChanged;
        Profiles.CollectionChanged += OnProfilesCollectionChanged;
        SelectedProfiles.CollectionChanged += OnSelectedProfilesCollectionChanged;
        isApplyingState = false;
        SelectedCertificateValidityOption = CertificateValidityOptions.First(option => option.Months == 6);
    }

    /// <summary>
    /// Gets the ordered Autopilot profiles available for deployment.
    /// </summary>
    public ObservableCollection<AutopilotProfileEntryViewModel> Profiles { get; } = [];

    /// <summary>
    /// Gets the currently selected profile rows for bulk UI actions.
    /// </summary>
    public ObservableCollection<AutopilotProfileEntryViewModel> SelectedProfiles { get; } = [];

    /// <summary>
    /// Gets the fixed certificate validity options available for managed Autopilot app certificates.
    /// </summary>
    public ObservableCollection<CertificateValidityOptionViewModel> CertificateValidityOptions { get; } =
    [
        new(1, "1 month"),
        new(3, "3 months"),
        new(6, "6 months"),
        new(12, "12 months")
    ];

    public bool IsAutopilotSectionEnabled => IsAutopilotEnabled;
    public bool HasProfiles => Profiles.Count > 0;
    public Visibility EmptyProfilesVisibility => HasProfiles ? Visibility.Collapsed : Visibility.Visible;
    public Visibility ProfilesVisibility => HasProfiles ? Visibility.Visible : Visibility.Collapsed;
    public bool IsBusy => IsImporting || IsDownloading || IsConnectingTenant;
    public Visibility BusyStatusVisibility => IsBusy ? Visibility.Visible : Visibility.Collapsed;
    public string BusyStatusText => IsImporting
        ? ImportingStatusText
        : IsDownloading
            ? DownloadingStatusText
            : IsConnectingTenant
                ? ConnectingTenantStatusText
                : string.Empty;
    public bool UseJsonProfileProvisioning
    {
        get => provisioningMode == AutopilotProvisioningMode.JsonProfile;
        set
        {
            if (value)
            {
                SetProvisioningMode(AutopilotProvisioningMode.JsonProfile);
            }
            else if (UseJsonProfileProvisioning && !UseHardwareHashUploadProvisioning)
            {
                OnPropertyChanged();
            }
        }
    }

    public bool UseHardwareHashUploadProvisioning
    {
        get => provisioningMode == AutopilotProvisioningMode.HardwareHashUpload;
        set
        {
            if (value)
            {
                SetProvisioningMode(AutopilotProvisioningMode.HardwareHashUpload);
            }
            else if (UseHardwareHashUploadProvisioning && !UseJsonProfileProvisioning)
            {
                OnPropertyChanged();
            }
        }
    }

    public bool IsJsonProfileMode => provisioningMode == AutopilotProvisioningMode.JsonProfile;
    public bool IsHardwareHashUploadMode => provisioningMode == AutopilotProvisioningMode.HardwareHashUpload;
    public bool IsHardwareHashCertificateExpired => hardwareHashUploadSettings.ActiveCertificate?.ExpiresOnUtc is DateTimeOffset expiresOnUtc &&
                                                    expiresOnUtc <= DateTimeOffset.UtcNow;
    public Visibility JsonProfileSettingsVisibility => IsJsonProfileMode ? Visibility.Visible : Visibility.Collapsed;
    public Visibility HardwareHashSettingsVisibility => IsHardwareHashUploadMode ? Visibility.Visible : Visibility.Collapsed;
    public Visibility HardwareHashCertificateWarningVisibility => IsHardwareHashCertificateExpired ? Visibility.Visible : Visibility.Collapsed;
    public string ManagedAppRegistrationName => AutopilotHardwareHashUploadSettings.ManagedAppRegistrationDisplayName;
    public string TenantStatusText => HasTenantRegistration
        ? localizationService.FormatString("Autopilot.HardwareHashTenantConnectedFormat", hardwareHashUploadSettings.Tenant.TenantId!)
        : localizationService.GetString("Autopilot.HardwareHashTenantNotConnected");
    public string AppRegistrationStatusText => string.IsNullOrWhiteSpace(hardwareHashUploadSettings.Tenant.ApplicationObjectId)
        ? localizationService.GetString("Autopilot.HardwareHashAppRegistrationMissing")
        : localizationService.FormatString("Autopilot.HardwareHashAppRegistrationFoundFormat", ManagedAppRegistrationName);
    public string TenantOnboardingStatusText => CreateTenantOnboardingStatusText();
    public string CertificateStatusText => CreateCertificateStatusText();
    public string DefaultGroupTagText => string.IsNullOrWhiteSpace(hardwareHashUploadSettings.DefaultGroupTag)
        ? localizationService.GetString("Autopilot.HardwareHashDefaultGroupTagNone")
        : hardwareHashUploadSettings.DefaultGroupTag!;
    public string KnownGroupTagsText => hardwareHashUploadSettings.KnownGroupTags.Count == 0
        ? localizationService.GetString("Autopilot.HardwareHashKnownGroupTagsNone")
        : string.Join(", ", hardwareHashUploadSettings.KnownGroupTags);

    private bool HasTenantRegistration => !string.IsNullOrWhiteSpace(hardwareHashUploadSettings.Tenant.TenantId) &&
                                          !string.IsNullOrWhiteSpace(hardwareHashUploadSettings.Tenant.ClientId);

    [ObservableProperty]
    public partial string PageTitle { get; set; }

    [ObservableProperty]
    public partial string AutopilotHeader { get; set; }

    [ObservableProperty]
    public partial string AutopilotDescription { get; set; }

    [ObservableProperty]
    public partial string EnableAutopilotText { get; set; }

    [ObservableProperty]
    public partial string JsonProfileHeader { get; set; }

    [ObservableProperty]
    public partial string JsonProfileDescription { get; set; }

    [ObservableProperty]
    public partial string JsonProfileEnableText { get; set; }

    [ObservableProperty]
    public partial string HardwareHashHeader { get; set; }

    [ObservableProperty]
    public partial string HardwareHashDescription { get; set; }

    [ObservableProperty]
    public partial string HardwareHashEnableText { get; set; }

    [ObservableProperty]
    public partial string ConnectTenantButtonText { get; set; }

    [ObservableProperty]
    public partial string ConnectingTenantStatusText { get; set; }

    [ObservableProperty]
    public partial string CertificateValidityLabel { get; set; }

    [ObservableProperty]
    public partial string CreateCertificateButtonText { get; set; }

    [ObservableProperty]
    public partial string RetireCertificateButtonText { get; set; }

    [ObservableProperty]
    public partial string TenantStatusLabel { get; set; }

    [ObservableProperty]
    public partial string AppRegistrationLabel { get; set; }

    [ObservableProperty]
    public partial string TenantOnboardingStatusLabel { get; set; }

    [ObservableProperty]
    public partial string CertificateStatusLabel { get; set; }

    [ObservableProperty]
    public partial string CertificateExpiredWarningText { get; set; }

    [ObservableProperty]
    public partial string DefaultGroupTagLabel { get; set; }

    [ObservableProperty]
    public partial string KnownGroupTagsLabel { get; set; }

    [ObservableProperty]
    public partial string ImportButtonText { get; set; }

    [ObservableProperty]
    public partial string DownloadButtonText { get; set; }

    [ObservableProperty]
    public partial string RemoveButtonText { get; set; }

    [ObservableProperty]
    public partial string ActionsHeader { get; set; }

    [ObservableProperty]
    public partial string ImportingStatusText { get; set; }

    [ObservableProperty]
    public partial string DownloadingStatusText { get; set; }

    [ObservableProperty]
    public partial string RemoveProfileConfirmationTitle { get; set; }

    [ObservableProperty]
    public partial string RemoveProfileConfirmationPrimaryButton { get; set; }

    [ObservableProperty]
    public partial string RemoveProfilesConfirmationTitle { get; set; }

    [ObservableProperty]
    public partial string RemoveProfilesConfirmationPrimaryButton { get; set; }

    [ObservableProperty]
    public partial string DefaultProfileLabel { get; set; }

    [ObservableProperty]
    public partial string ProfilesLabel { get; set; }

    [ObservableProperty]
    public partial string EmptyProfilesText { get; set; }

    [ObservableProperty]
    public partial string ProfileNameColumnHeader { get; set; }

    [ObservableProperty]
    public partial string ProfileSourceColumnHeader { get; set; }

    [ObservableProperty]
    public partial string ProfileImportedColumnHeader { get; set; }

    [ObservableProperty]
    public partial string ProfileFolderColumnHeader { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAutopilotSectionEnabled))]
    [NotifyPropertyChangedFor(nameof(JsonProfileSettingsVisibility))]
    [NotifyPropertyChangedFor(nameof(HardwareHashSettingsVisibility))]
    [NotifyCanExecuteChangedFor(nameof(ImportProfileCommand))]
    [NotifyCanExecuteChangedFor(nameof(DownloadProfilesCommand))]
    [NotifyCanExecuteChangedFor(nameof(RemoveSelectedProfilesCommand))]
    [NotifyCanExecuteChangedFor(nameof(ConnectTenantCommand))]
    [NotifyCanExecuteChangedFor(nameof(CreateCertificateCommand))]
    [NotifyCanExecuteChangedFor(nameof(RetireActiveCertificateCommand))]
    public partial bool IsAutopilotEnabled { get; set; }

    [ObservableProperty]
    public partial AutopilotProfileEntryViewModel? SelectedDefaultProfile { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ImportProfileCommand))]
    [NotifyCanExecuteChangedFor(nameof(DownloadProfilesCommand))]
    [NotifyCanExecuteChangedFor(nameof(RemoveSelectedProfilesCommand))]
    [NotifyPropertyChangedFor(nameof(IsBusy))]
    [NotifyPropertyChangedFor(nameof(BusyStatusText))]
    [NotifyPropertyChangedFor(nameof(BusyStatusVisibility))]
    [NotifyCanExecuteChangedFor(nameof(ConnectTenantCommand))]
    [NotifyCanExecuteChangedFor(nameof(CreateCertificateCommand))]
    [NotifyCanExecuteChangedFor(nameof(RetireActiveCertificateCommand))]
    public partial bool IsImporting { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ImportProfileCommand))]
    [NotifyCanExecuteChangedFor(nameof(DownloadProfilesCommand))]
    [NotifyCanExecuteChangedFor(nameof(RemoveSelectedProfilesCommand))]
    [NotifyPropertyChangedFor(nameof(IsBusy))]
    [NotifyPropertyChangedFor(nameof(BusyStatusText))]
    [NotifyPropertyChangedFor(nameof(BusyStatusVisibility))]
    [NotifyCanExecuteChangedFor(nameof(ConnectTenantCommand))]
    [NotifyCanExecuteChangedFor(nameof(CreateCertificateCommand))]
    [NotifyCanExecuteChangedFor(nameof(RetireActiveCertificateCommand))]
    public partial bool IsDownloading { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ImportProfileCommand))]
    [NotifyCanExecuteChangedFor(nameof(DownloadProfilesCommand))]
    [NotifyCanExecuteChangedFor(nameof(RemoveSelectedProfilesCommand))]
    [NotifyPropertyChangedFor(nameof(IsBusy))]
    [NotifyPropertyChangedFor(nameof(BusyStatusText))]
    [NotifyPropertyChangedFor(nameof(BusyStatusVisibility))]
    [NotifyCanExecuteChangedFor(nameof(ConnectTenantCommand))]
    [NotifyCanExecuteChangedFor(nameof(CreateCertificateCommand))]
    [NotifyCanExecuteChangedFor(nameof(RetireActiveCertificateCommand))]
    public partial bool IsConnectingTenant { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateCertificateCommand))]
    public partial CertificateValidityOptionViewModel? SelectedCertificateValidityOption { get; set; }

    /// <summary>
    /// Releases subscriptions to localization, configuration state, and profile collections.
    /// </summary>
    public void Dispose()
    {
        localizationService.LanguageChanged -= OnLanguageChanged;
        configurationStateService.StateChanged -= OnConfigurationStateChanged;
        Profiles.CollectionChanged -= OnProfilesCollectionChanged;
        SelectedProfiles.CollectionChanged -= OnSelectedProfilesCollectionChanged;
    }

    [RelayCommand(CanExecute = nameof(CanImportProfile))]
    private async Task ImportProfileAsync()
    {
        string? filePath = await filePickerService.PickOpenFileAsync(
            new FileOpenPickerRequest(localizationService.GetString("Autopilot.ImportPickerTitle"), [".json"]));
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return;
        }

        IsImporting = true;
        try
        {
            AutopilotProfileSettings profile = await autopilotProfileImportService.ImportFromJsonFileAsync(filePath);
            MergeProfiles([profile]);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or ArgumentException)
        {
            await dialogService.ShowMessageAsync(new DialogRequest(
                localizationService.GetString("Autopilot.ImportFailedTitle"),
                localizationService.FormatString("Autopilot.ImportFailedMessage", ex.Message)));
        }
        finally
        {
            IsImporting = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanDownloadProfiles))]
    private async Task DownloadProfilesAsync()
    {
        IsDownloading = true;
        try
        {
            logger.Information("Starting Autopilot profile download from tenant.");
            IReadOnlyList<AutopilotProfileSettings>? availableProfiles =
                await tenantDownloadDialogService.DownloadAsync(autopilotTenantProfileService.DownloadFromTenantAsync);
            if (availableProfiles is null)
            {
                logger.Information("Autopilot tenant download was canceled.");
                return;
            }

            if (availableProfiles.Count == 0)
            {
                logger.Information("Autopilot tenant download completed. ProfileCount=0");
                await dialogService.ShowMessageAsync(new DialogRequest(
                    localizationService.GetString("Autopilot.DownloadNoProfilesTitle"),
                    localizationService.GetString("Autopilot.DownloadCompletedNoProfiles")));
                return;
            }

            IReadOnlyList<AutopilotProfileSettings>? selectedProfiles =
                await profileSelectionDialogService.PickProfilesAsync(availableProfiles);
            if (selectedProfiles is null)
            {
                logger.Information("Autopilot tenant download was canceled from the profile selection dialog.");
                return;
            }

            MergeProfiles(selectedProfiles);
            logger.Information(
                "Autopilot tenant download completed. RetrievedProfileCount={RetrievedProfileCount}, ImportedProfileCount={ImportedProfileCount}",
                availableProfiles.Count,
                selectedProfiles.Count);
        }
        catch (OperationCanceledException)
        {
            logger.Information("Autopilot tenant download was canceled.");
            return;
        }
        catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException or JsonException or AuthenticationFailedException)
        {
            string failureMessage = localizationService.FormatString("Autopilot.DownloadFailedFormat", ex.Message);
            logger.Error(ex, "Autopilot tenant download failed.");
            await dialogService.ShowMessageAsync(new DialogRequest(
                localizationService.GetString("Autopilot.DownloadFailedTitle"),
                failureMessage));
            return;
        }
        finally
        {
            IsDownloading = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanRemoveSelectedProfiles))]
    private async Task RemoveSelectedProfilesAsync()
    {
        AutopilotProfileEntryViewModel[] profilesToRemove = SelectedProfiles.ToArray();
        if (profilesToRemove.Length == 0)
        {
            return;
        }

        bool isSingleProfileRemoval = profilesToRemove.Length == 1;
        bool confirmed = await dialogService.ConfirmAsync(new ConfirmationDialogRequest(
            isSingleProfileRemoval ? RemoveProfileConfirmationTitle : RemoveProfilesConfirmationTitle,
            isSingleProfileRemoval
                ? localizationService.FormatString("Autopilot.RemoveConfirmationMessageFormat", profilesToRemove[0].DisplayName)
                : localizationService.FormatString("Autopilot.RemoveProfilesConfirmationMessageFormat", profilesToRemove.Length),
            isSingleProfileRemoval ? RemoveProfileConfirmationPrimaryButton : RemoveProfilesConfirmationPrimaryButton,
            localizationService.GetString("Common.Cancel")));
        if (!confirmed)
        {
            return;
        }

        HashSet<string> removedProfileIds = profilesToRemove
            .Select(profile => profile.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (AutopilotProfileEntryViewModel profile in profilesToRemove)
        {
            Profiles.Remove(profile);
        }

        SelectedProfiles.Clear();

        if (SelectedDefaultProfile is null ||
            removedProfileIds.Contains(SelectedDefaultProfile.Id))
        {
            SelectedDefaultProfile = Profiles.FirstOrDefault();
        }

        SaveState();
    }

    [RelayCommand(CanExecute = nameof(CanConnectTenant))]
    private async Task ConnectTenantAsync()
    {
        IsConnectingTenant = true;
        try
        {
            logger.Information("Starting Autopilot hardware hash tenant onboarding.");
            AutopilotTenantOnboardingResult result = await autopilotTenantOnboardingService.ConnectAsync(hardwareHashUploadSettings);
            hardwareHashUploadSettings = result.Settings;
            tenantOnboardingStatus = result.Status;
            RefreshHardwareHashUploadState();
            SaveState();
            logger.Information(
                "Autopilot hardware hash tenant onboarding updated. Status={Status}, TenantId={TenantId}, ApplicationObjectId={ApplicationObjectId}",
                result.Status,
                result.Settings.Tenant.TenantId,
                result.Settings.Tenant.ApplicationObjectId);
            await dialogService.ShowMessageAsync(new DialogRequest(
                GetTenantOnboardingDialogTitle(result.Status),
                result.Message));
        }
        catch (Exception ex) when (ex is AuthenticationFailedException or HttpRequestException or InvalidOperationException or JsonException)
        {
            logger.Error(ex, "Autopilot hardware hash tenant onboarding failed.");
            await dialogService.ShowMessageAsync(new DialogRequest(
                localizationService.GetString("Autopilot.HardwareHashOnboardingFailedTitle"),
                localizationService.FormatString("Autopilot.HardwareHashOnboardingFailedMessageFormat", ex.Message)));
        }
        finally
        {
            IsConnectingTenant = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanCreateCertificate))]
    private async Task CreateCertificateAsync()
    {
        string? outputPath = await filePickerService.PickSaveFileAsync(new FileSavePickerRequest(
            localizationService.GetString("Autopilot.HardwareHashCertificateSavePickerTitle"),
            "foundry-osd-autopilot-registration.pfx",
            [new FilePickerTypeChoice(localizationService.GetString("Autopilot.HardwareHashCertificatePfxFileType"), [".pfx"])],
            ".pfx"));
        if (string.IsNullOrWhiteSpace(outputPath) || SelectedCertificateValidityOption is null)
        {
            return;
        }

        IsConnectingTenant = true;
        try
        {
            AutopilotCertificateCreationResult result = await autopilotTenantOnboardingService.CreateCertificateAsync(
                hardwareHashUploadSettings,
                outputPath,
                SelectedCertificateValidityOption.Months);
            hardwareHashUploadSettings = result.Settings;
            RefreshHardwareHashUploadState();
            SaveState();

            await dialogService.ShowMessageAsync(new DialogRequest(
                localizationService.GetString("Autopilot.HardwareHashCertificateCreatedTitle"),
                localizationService.FormatString(
                    "Autopilot.HardwareHashCertificateCreatedMessageFormat",
                    result.GeneratedPassword,
                    outputPath)));
        }
        catch (Exception ex) when (ex is AuthenticationFailedException or HttpRequestException or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            logger.Error("Autopilot hardware hash certificate creation failed. ErrorType={ErrorType}", ex.GetType().Name);
            await dialogService.ShowMessageAsync(new DialogRequest(
                localizationService.GetString("Autopilot.HardwareHashCertificateCreateFailedTitle"),
                localizationService.FormatString("Autopilot.HardwareHashCertificateCreateFailedMessageFormat", ex.Message)));
        }
        finally
        {
            IsConnectingTenant = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanRetireActiveCertificate))]
    private async Task RetireActiveCertificateAsync()
    {
        bool confirmed = await dialogService.ConfirmAsync(new ConfirmationDialogRequest(
            localizationService.GetString("Autopilot.HardwareHashRetireCertificateConfirmationTitle"),
            localizationService.GetString("Autopilot.HardwareHashRetireCertificateConfirmationMessage"),
            localizationService.GetString("Autopilot.HardwareHashRetireCertificateConfirmationPrimary"),
            localizationService.GetString("Common.Cancel")));
        if (!confirmed)
        {
            return;
        }

        IsConnectingTenant = true;
        try
        {
            hardwareHashUploadSettings = await autopilotTenantOnboardingService.RetireActiveCertificateAsync(hardwareHashUploadSettings);
            tenantOnboardingStatus = AutopilotTenantOnboardingStatus.ActiveCertificateMissing;
            RefreshHardwareHashUploadState();
            SaveState();

            await dialogService.ShowMessageAsync(new DialogRequest(
                localizationService.GetString("Autopilot.HardwareHashCertificateRetiredTitle"),
                localizationService.GetString("Autopilot.HardwareHashCertificateRetiredMessage")));
        }
        catch (Exception ex) when (ex is AuthenticationFailedException or HttpRequestException or InvalidOperationException or JsonException)
        {
            logger.Error(ex, "Autopilot hardware hash certificate retirement failed.");
            await dialogService.ShowMessageAsync(new DialogRequest(
                localizationService.GetString("Autopilot.HardwareHashCertificateRetireFailedTitle"),
                localizationService.FormatString("Autopilot.HardwareHashCertificateRetireFailedMessageFormat", ex.Message)));
        }
        finally
        {
            IsConnectingTenant = false;
        }
    }

    partial void OnIsAutopilotEnabledChanged(bool value)
    {
        ImportProfileCommand.NotifyCanExecuteChanged();
        DownloadProfilesCommand.NotifyCanExecuteChanged();
        RemoveSelectedProfilesCommand.NotifyCanExecuteChanged();
        ConnectTenantCommand.NotifyCanExecuteChanged();
        CreateCertificateCommand.NotifyCanExecuteChanged();
        RetireActiveCertificateCommand.NotifyCanExecuteChanged();
        SaveState();
    }

    partial void OnSelectedDefaultProfileChanged(AutopilotProfileEntryViewModel? value)
    {
        SaveState();
    }

    private void ApplyState(AutopilotSettings settings)
    {
        isApplyingState = true;
        try
        {
            IsAutopilotEnabled = settings.IsEnabled;
            provisioningMode = Enum.IsDefined(settings.ProvisioningMode)
                ? settings.ProvisioningMode
                : AutopilotProvisioningMode.JsonProfile;
            hardwareHashUploadSettings = settings.HardwareHashUpload ?? new AutopilotHardwareHashUploadSettings();
            ReplaceProfiles(
                settings.Profiles.Select(AutopilotProfileEntryViewModel.FromSettings),
                settings.DefaultProfileId);
            RefreshProvisioningModeState();
            RefreshHardwareHashUploadState();
        }
        finally
        {
            isApplyingState = false;
        }
    }

    private void MergeProfiles(IReadOnlyList<AutopilotProfileSettings> incomingProfiles)
    {
        Dictionary<string, AutopilotProfileEntryViewModel> mergedProfiles = Profiles
            .ToDictionary(profile => profile.Id, StringComparer.OrdinalIgnoreCase);

        foreach (AutopilotProfileSettings incomingProfile in incomingProfiles)
        {
            // Tenant downloads and file imports are keyed by Graph profile ID so newer payloads replace older copies.
            mergedProfiles[incomingProfile.Id] = AutopilotProfileEntryViewModel.FromSettings(incomingProfile);
        }

        string? preferredDefaultProfileId = SelectedDefaultProfile?.Id ?? incomingProfiles.FirstOrDefault()?.Id;
        ReplaceProfiles(mergedProfiles.Values, preferredDefaultProfileId);
        SaveState();
    }

    private void ReplaceProfiles(
        IEnumerable<AutopilotProfileEntryViewModel> profiles,
        string? preferredDefaultProfileId)
    {
        AutopilotProfileEntryViewModel[] orderedProfiles = profiles
            .OrderBy(profile => profile.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(profile => profile.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Profiles.Clear();
        foreach (AutopilotProfileEntryViewModel profile in orderedProfiles)
        {
            Profiles.Add(profile);
        }

        SelectedProfiles.Clear();

        SelectedDefaultProfile = Profiles.FirstOrDefault(profile =>
                                     string.Equals(profile.Id, preferredDefaultProfileId, StringComparison.OrdinalIgnoreCase))
                                 ?? Profiles.FirstOrDefault();

        RefreshProfileState();
    }

    private void SaveState()
    {
        if (isApplyingState)
        {
            return;
        }

        isSavingState = true;
        try
        {
            configurationStateService.UpdateAutopilot(new AutopilotSettings
            {
                IsEnabled = IsAutopilotEnabled,
                ProvisioningMode = provisioningMode,
                DefaultProfileId = SelectedDefaultProfile?.Id,
                Profiles = Profiles.Select(profile => profile.ToSettings()).ToArray(),
                HardwareHashUpload = hardwareHashUploadSettings
            });
        }
        finally
        {
            isSavingState = false;
        }
    }

    private void RefreshLocalizedText()
    {
        PageTitle = localizationService.GetString("AutopilotPage_Title.Text");
        AutopilotHeader = localizationService.GetString("Autopilot.Header");
        AutopilotDescription = localizationService.GetString("Autopilot.Description");
        EnableAutopilotText = localizationService.GetString("Autopilot.EnableLabel");
        JsonProfileHeader = localizationService.GetString("Autopilot.JsonProfileHeader");
        JsonProfileDescription = localizationService.GetString("Autopilot.JsonProfileDescription");
        JsonProfileEnableText = localizationService.GetString("Autopilot.JsonProfileEnableLabel");
        HardwareHashHeader = localizationService.GetString("Autopilot.HardwareHashHeader");
        HardwareHashDescription = localizationService.GetString("Autopilot.HardwareHashDescription");
        HardwareHashEnableText = localizationService.GetString("Autopilot.HardwareHashEnableLabel");
        ConnectTenantButtonText = localizationService.GetString("Autopilot.HardwareHashConnectTenantButton");
        ConnectingTenantStatusText = localizationService.GetString("Autopilot.HardwareHashConnectingTenantStatus");
        CertificateValidityLabel = localizationService.GetString("Autopilot.HardwareHashCertificateValidityLabel");
        CreateCertificateButtonText = localizationService.GetString("Autopilot.HardwareHashCreateCertificateButton");
        RetireCertificateButtonText = localizationService.GetString("Autopilot.HardwareHashRetireCertificateButton");
        TenantStatusLabel = localizationService.GetString("Autopilot.HardwareHashTenantStatusLabel");
        AppRegistrationLabel = localizationService.GetString("Autopilot.HardwareHashAppRegistrationLabel");
        TenantOnboardingStatusLabel = localizationService.GetString("Autopilot.HardwareHashOnboardingStatusLabel");
        CertificateStatusLabel = localizationService.GetString("Autopilot.HardwareHashCertificateStatusLabel");
        CertificateExpiredWarningText = localizationService.GetString("Autopilot.HardwareHashCertificateExpiredWarning");
        DefaultGroupTagLabel = localizationService.GetString("Autopilot.HardwareHashDefaultGroupTagLabel");
        KnownGroupTagsLabel = localizationService.GetString("Autopilot.HardwareHashKnownGroupTagsLabel");
        ImportButtonText = localizationService.GetString("Autopilot.ImportButton");
        DownloadButtonText = localizationService.GetString("Autopilot.DownloadButton");
        RemoveButtonText = localizationService.GetString("Autopilot.RemoveButton");
        ActionsHeader = localizationService.GetString("Autopilot.ActionsHeader");
        ImportingStatusText = localizationService.GetString("Autopilot.ImportingStatus");
        DownloadingStatusText = localizationService.GetString("Autopilot.DownloadingStatus");
        RemoveProfileConfirmationTitle = localizationService.GetString("Autopilot.RemoveConfirmationTitle");
        RemoveProfileConfirmationPrimaryButton = localizationService.GetString("Autopilot.RemoveConfirmationPrimaryButton");
        RemoveProfilesConfirmationTitle = localizationService.GetString("Autopilot.RemoveProfilesConfirmationTitle");
        RemoveProfilesConfirmationPrimaryButton = localizationService.GetString("Autopilot.RemoveProfilesConfirmationPrimaryButton");
        DefaultProfileLabel = localizationService.GetString("Autopilot.DefaultProfileLabel");
        ProfilesLabel = localizationService.GetString("Autopilot.ProfilesLabel");
        EmptyProfilesText = localizationService.GetString("Autopilot.ProfilesEmptyLabel");
        ProfileNameColumnHeader = localizationService.GetString("Autopilot.ColumnName");
        ProfileSourceColumnHeader = localizationService.GetString("Autopilot.ColumnSource");
        ProfileImportedColumnHeader = localizationService.GetString("Autopilot.ColumnImported");
        ProfileFolderColumnHeader = localizationService.GetString("Autopilot.ColumnFolder");
        OnPropertyChanged(nameof(BusyStatusText));
        RefreshHardwareHashUploadState();
    }

    private void SetProvisioningMode(AutopilotProvisioningMode mode)
    {
        if (provisioningMode == mode)
        {
            return;
        }

        provisioningMode = mode;
        RefreshProvisioningModeState();
        SaveState();
    }

    private void RefreshProvisioningModeState()
    {
        OnPropertyChanged(nameof(UseJsonProfileProvisioning));
        OnPropertyChanged(nameof(UseHardwareHashUploadProvisioning));
        OnPropertyChanged(nameof(IsJsonProfileMode));
        OnPropertyChanged(nameof(IsHardwareHashUploadMode));
        OnPropertyChanged(nameof(JsonProfileSettingsVisibility));
        OnPropertyChanged(nameof(HardwareHashSettingsVisibility));
        ImportProfileCommand.NotifyCanExecuteChanged();
        DownloadProfilesCommand.NotifyCanExecuteChanged();
        RemoveSelectedProfilesCommand.NotifyCanExecuteChanged();
        ConnectTenantCommand.NotifyCanExecuteChanged();
        CreateCertificateCommand.NotifyCanExecuteChanged();
        RetireActiveCertificateCommand.NotifyCanExecuteChanged();
    }

    private void RefreshHardwareHashUploadState()
    {
        OnPropertyChanged(nameof(TenantStatusText));
        OnPropertyChanged(nameof(AppRegistrationStatusText));
        OnPropertyChanged(nameof(TenantOnboardingStatusText));
        OnPropertyChanged(nameof(CertificateStatusText));
        OnPropertyChanged(nameof(IsHardwareHashCertificateExpired));
        OnPropertyChanged(nameof(HardwareHashCertificateWarningVisibility));
        OnPropertyChanged(nameof(DefaultGroupTagText));
        OnPropertyChanged(nameof(KnownGroupTagsText));
        RetireActiveCertificateCommand.NotifyCanExecuteChanged();
    }

    private string CreateCertificateStatusText()
    {
        AutopilotCertificateMetadata? certificate = hardwareHashUploadSettings.ActiveCertificate;
        if (certificate is null)
        {
            return localizationService.GetString("Autopilot.HardwareHashCertificateMissing");
        }

        if (certificate.ExpiresOnUtc is null)
        {
            return localizationService.GetString("Autopilot.HardwareHashCertificateExpirationMissing");
        }

        return certificate.ExpiresOnUtc <= DateTimeOffset.UtcNow
            ? localizationService.FormatString("Autopilot.HardwareHashCertificateExpiredFormat", certificate.ExpiresOnUtc.Value.LocalDateTime)
            : localizationService.FormatString("Autopilot.HardwareHashCertificateValidFormat", certificate.ExpiresOnUtc.Value.LocalDateTime);
    }

    private string CreateTenantOnboardingStatusText()
    {
        return tenantOnboardingStatus switch
        {
            AutopilotTenantOnboardingStatus.Ready => localizationService.GetString("Autopilot.HardwareHashOnboardingStatusReady"),
            AutopilotTenantOnboardingStatus.AppRegistrationMissing => localizationService.GetString("Autopilot.HardwareHashOnboardingStatusAppMissing"),
            AutopilotTenantOnboardingStatus.AdoptionRequired => localizationService.GetString("Autopilot.HardwareHashOnboardingStatusAdoptionRequired"),
            AutopilotTenantOnboardingStatus.PermissionMissing => localizationService.GetString("Autopilot.HardwareHashOnboardingStatusPermissionMissing"),
            AutopilotTenantOnboardingStatus.ConsentMissing => localizationService.GetString("Autopilot.HardwareHashOnboardingStatusConsentMissing"),
            AutopilotTenantOnboardingStatus.ServicePrincipalUnavailable => localizationService.GetString("Autopilot.HardwareHashOnboardingStatusServicePrincipalUnavailable"),
            AutopilotTenantOnboardingStatus.ActiveCertificateMissing => localizationService.GetString("Autopilot.HardwareHashOnboardingStatusCertificateMissing"),
            AutopilotTenantOnboardingStatus.ActiveCertificateNotFound => localizationService.GetString("Autopilot.HardwareHashOnboardingStatusCertificateNotFound"),
            AutopilotTenantOnboardingStatus.ActiveCertificateExpired => localizationService.GetString("Autopilot.HardwareHashOnboardingStatusCertificateExpired"),
            AutopilotTenantOnboardingStatus.MultipleFoundryCertificatesNeedSelection => localizationService.GetString("Autopilot.HardwareHashOnboardingStatusMultipleCertificates"),
            _ => localizationService.GetString("Autopilot.HardwareHashOnboardingStatusNotChecked")
        };
    }

    private string GetTenantOnboardingDialogTitle(AutopilotTenantOnboardingStatus status)
    {
        return status == AutopilotTenantOnboardingStatus.Ready
            ? localizationService.GetString("Autopilot.HardwareHashOnboardingCompletedTitle")
            : localizationService.GetString("Autopilot.HardwareHashOnboardingRequiresAttentionTitle");
    }

    private void RefreshProfileState()
    {
        OnPropertyChanged(nameof(HasProfiles));
        OnPropertyChanged(nameof(EmptyProfilesVisibility));
        OnPropertyChanged(nameof(ProfilesVisibility));
        RemoveSelectedProfilesCommand.NotifyCanExecuteChanged();
    }

    private void OnProfilesCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        RefreshProfileState();
    }

    private void OnSelectedProfilesCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        RemoveSelectedProfilesCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Replaces the selected profile rows after a XAML selection change.
    /// </summary>
    /// <param name="profiles">The selected profile view models.</param>
    public void ReplaceSelectedProfiles(IEnumerable<AutopilotProfileEntryViewModel> profiles)
    {
        SelectedProfiles.Clear();
        foreach (AutopilotProfileEntryViewModel profile in profiles)
        {
            SelectedProfiles.Add(profile);
        }
    }

    private void OnLanguageChanged(object? sender, ApplicationLanguageChangedEventArgs e)
    {
        RefreshLocalizedText();
    }

    private void OnConfigurationStateChanged(object? sender, EventArgs e)
    {
        if (isSavingState)
        {
            return;
        }

        ApplyState(configurationStateService.Current.Autopilot);
    }

    private bool CanImportProfile()
    {
        return IsAutopilotEnabled && IsJsonProfileMode && !IsImporting && !IsDownloading;
    }

    private bool CanDownloadProfiles()
    {
        return IsAutopilotEnabled && IsJsonProfileMode && !IsImporting && !IsDownloading;
    }

    private bool CanRemoveSelectedProfiles()
    {
        return IsAutopilotEnabled && IsJsonProfileMode && !IsImporting && !IsDownloading && SelectedProfiles.Count > 0;
    }

    private bool CanConnectTenant()
    {
        return IsAutopilotEnabled && IsHardwareHashUploadMode && !IsBusy;
    }

    private bool CanCreateCertificate()
    {
        return IsAutopilotEnabled &&
               IsHardwareHashUploadMode &&
               !IsBusy &&
               SelectedCertificateValidityOption is not null &&
               !string.IsNullOrWhiteSpace(hardwareHashUploadSettings.Tenant.ApplicationObjectId);
    }

    private bool CanRetireActiveCertificate()
    {
        return IsAutopilotEnabled &&
               IsHardwareHashUploadMode &&
               !IsBusy &&
               !string.IsNullOrWhiteSpace(hardwareHashUploadSettings.Tenant.ApplicationObjectId) &&
               !string.IsNullOrWhiteSpace(hardwareHashUploadSettings.ActiveCertificate?.KeyId);
    }

    public sealed record CertificateValidityOptionViewModel(int Months, string DisplayName);
}
