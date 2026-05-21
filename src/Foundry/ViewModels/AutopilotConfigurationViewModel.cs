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
using Microsoft.UI.Xaml.Media;
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
    private readonly IAutopilotHardwareHashGraphSessionService hardwareHashGraphSessionService;
    private readonly IAutopilotTenantOperationDialogService tenantOperationDialogService;
    private readonly IAutopilotCertificateDialogService certificateDialogService;
    private readonly IAutopilotProfileSelectionDialogService profileSelectionDialogService;
    private readonly IAutopilotHardwareHashSessionState hardwareHashSessionState;
    private readonly IFilePickerService filePickerService;
    private readonly IDialogService dialogService;
    private readonly IApplicationLocalizationService localizationService;
    private readonly ILogger logger;
    private bool isApplyingState = true;
    private bool isSavingState;
    private AutopilotProvisioningMode provisioningMode = AutopilotProvisioningMode.JsonProfile;
    private AutopilotHardwareHashUploadSettings hardwareHashUploadSettings = new();
    private AutopilotTenantOnboardingStatus? tenantOnboardingStatus;
    private AutopilotPfxValidationCode bootMediaCertificateValidationCode = AutopilotPfxValidationCode.PfxRequired;
    private bool isBootMediaCertificateFileMissing;

    public AutopilotConfigurationViewModel(
        IFoundryConfigurationStateService configurationStateService,
        IAutopilotProfileImportService autopilotProfileImportService,
        IAutopilotTenantProfileService autopilotTenantProfileService,
        IAutopilotTenantOnboardingService autopilotTenantOnboardingService,
        IAutopilotHardwareHashGraphSessionService hardwareHashGraphSessionService,
        IAutopilotTenantOperationDialogService tenantOperationDialogService,
        IAutopilotCertificateDialogService certificateDialogService,
        IAutopilotProfileSelectionDialogService profileSelectionDialogService,
        IAutopilotHardwareHashSessionState hardwareHashSessionState,
        IFilePickerService filePickerService,
        IDialogService dialogService,
        IApplicationLocalizationService localizationService,
        ILogger logger)
    {
        this.configurationStateService = configurationStateService;
        this.autopilotProfileImportService = autopilotProfileImportService;
        this.autopilotTenantProfileService = autopilotTenantProfileService;
        this.autopilotTenantOnboardingService = autopilotTenantOnboardingService;
        this.hardwareHashGraphSessionService = hardwareHashGraphSessionService;
        this.tenantOperationDialogService = tenantOperationDialogService;
        this.certificateDialogService = certificateDialogService;
        this.profileSelectionDialogService = profileSelectionDialogService;
        this.hardwareHashSessionState = hardwareHashSessionState;
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
        SelectedCertificates.CollectionChanged += OnSelectedCertificatesCollectionChanged;
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
    /// Gets app registration certificate credentials discovered from Microsoft Graph.
    /// </summary>
    public ObservableCollection<AutopilotCertificateEntryViewModel> Certificates { get; } = [];

    /// <summary>
    /// Gets the currently selected certificate rows for bulk UI actions.
    /// </summary>
    public ObservableCollection<AutopilotCertificateEntryViewModel> SelectedCertificates { get; } = [];

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

    /// <summary>
    /// Gets tenant-discovered Autopilot group tags available for the default deployment selection.
    /// </summary>
    public ObservableCollection<AutopilotGroupTagEntryViewModel> AvailableGroupTags { get; } = [];

    /// <summary>
    /// Gets default group tag choices, including the optional None choice.
    /// </summary>
    public ObservableCollection<AutopilotGroupTagEntryViewModel> DefaultGroupTagOptions { get; } = [];

    /// <summary>
    /// Gets tenant metadata displayed after a successful tenant connection.
    /// </summary>
    public ObservableCollection<AutopilotTenantDetailEntryViewModel> TenantDetails { get; } = [];

    public bool IsAutopilotSectionEnabled => IsAutopilotEnabled;
    public bool HasProfiles => Profiles.Count > 0;
    public Visibility EmptyProfilesVisibility => HasProfiles ? Visibility.Collapsed : Visibility.Visible;
    public Visibility ProfilesVisibility => HasProfiles ? Visibility.Visible : Visibility.Collapsed;
    public bool HasCertificates => Certificates.Count > 0;
    public Visibility CertificatesVisibility => HasCertificates ? Visibility.Visible : Visibility.Collapsed;
    public bool HasAvailableGroupTags => AvailableGroupTags.Count > 0;
    public Visibility AvailableGroupTagsVisibility => HasAvailableGroupTags ? Visibility.Visible : Visibility.Collapsed;
    public Visibility EmptyAvailableGroupTagsVisibility => HasAvailableGroupTags ? Visibility.Collapsed : Visibility.Visible;
    public Visibility ConnectedTenantDetailsVisibility => HasConnectedTenantInCurrentSession ? Visibility.Visible : Visibility.Collapsed;
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
    public Visibility BootMediaCertificateVisibility => HasConnectedTenantInCurrentSession &&
                                                        HasCertificates
        ? Visibility.Visible
        : Visibility.Collapsed;
    public string BootMediaCertificatePfxPath => hardwareHashUploadSettings.BootMediaCertificate.PfxPath ?? string.Empty;
    public string BootMediaCertificateStatusText => CreateBootMediaCertificateStatusText();
    public Brush BootMediaCertificateStatusForeground => ResolveBootMediaCertificateStatusBrush();
    public bool IsBootMediaCertificateReady => hardwareHashUploadSettings.BootMediaCertificate.ValidatedExpiresOnUtc is DateTimeOffset expiresOnUtc &&
                                               expiresOnUtc > DateTimeOffset.UtcNow &&
                                               !string.IsNullOrWhiteSpace(hardwareHashUploadSettings.BootMediaCertificate.PfxPath) &&
                                               !string.IsNullOrWhiteSpace(hardwareHashUploadSettings.BootMediaCertificate.PfxPassword) &&
                                               string.Equals(
                                                   NormalizeThumbprint(hardwareHashUploadSettings.ActiveCertificate?.Thumbprint),
                                                   NormalizeThumbprint(hardwareHashUploadSettings.BootMediaCertificate.ValidatedThumbprint),
                                                   StringComparison.OrdinalIgnoreCase);
    public string ManagedAppRegistrationName => AutopilotHardwareHashUploadSettings.ManagedAppRegistrationDisplayName;
    public string TenantStatusText => HasConnectedTenantInCurrentSession && HasTenantRegistration
        ? localizationService.GetString("Autopilot.HardwareHashTenantConnected")
        : localizationService.GetString("Autopilot.HardwareHashTenantNotConnected");
    public string TenantConnectionButtonText => HasConnectedTenantInCurrentSession
        ? DisconnectTenantButtonText
        : ConnectTenantButtonText;
    public string AppRegistrationStatusText => string.IsNullOrWhiteSpace(hardwareHashUploadSettings.Tenant.ApplicationObjectId)
        ? localizationService.GetString("Autopilot.HardwareHashAppRegistrationMissing")
        : localizationService.FormatString("Autopilot.HardwareHashAppRegistrationFoundFormat", ManagedAppRegistrationName);
    public string TenantOnboardingStatusText => CreateTenantOnboardingStatusText();
    public Brush TenantOnboardingStatusForeground => ResolveTenantOnboardingStatusBrush();
    public string CertificateStatusText => CreateCertificateStatusText();
    public Visibility CertificateStatusVisibility => string.IsNullOrWhiteSpace(CertificateStatusText) ? Visibility.Collapsed : Visibility.Visible;
    public Brush CertificateStatusForeground => ResolveCertificateStatusBrush();
    public string DefaultGroupTagText => string.IsNullOrWhiteSpace(hardwareHashUploadSettings.DefaultGroupTag)
        ? localizationService.GetString("Autopilot.HardwareHashDefaultGroupTagNone")
        : hardwareHashUploadSettings.DefaultGroupTag!;
    public string KnownGroupTagsText => hardwareHashUploadSettings.KnownGroupTags.Count == 0
        ? localizationService.GetString("Autopilot.HardwareHashKnownGroupTagsNone")
        : string.Join(", ", hardwareHashUploadSettings.KnownGroupTags);

    private bool HasTenantRegistration => !string.IsNullOrWhiteSpace(hardwareHashUploadSettings.Tenant.TenantId) &&
                                          !string.IsNullOrWhiteSpace(hardwareHashUploadSettings.Tenant.ClientId);

    private bool HasConnectedTenantInCurrentSession
    {
        get => hardwareHashSessionState.HasConnectedTenant;
        set => hardwareHashSessionState.HasConnectedTenant = value;
    }

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
    public partial string ActionsDescription { get; set; }

    [ObservableProperty]
    public partial string DefaultProfileDescription { get; set; }

    [ObservableProperty]
    public partial string ProfilesDescription { get; set; }

    [ObservableProperty]
    public partial string HardwareHashHeader { get; set; }

    [ObservableProperty]
    public partial string HardwareHashDescription { get; set; }

    [ObservableProperty]
    public partial string HardwareHashEnableText { get; set; }

    [ObservableProperty]
    public partial string ConnectTenantButtonText { get; set; }

    [ObservableProperty]
    public partial string DisconnectTenantButtonText { get; set; }

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
    public partial string TenantStatusDescription { get; set; }

    [ObservableProperty]
    public partial string TenantDetailsLabel { get; set; }

    [ObservableProperty]
    public partial string TenantDetailsDescription { get; set; }

    [ObservableProperty]
    public partial string TenantDetailsNameColumnHeader { get; set; }

    [ObservableProperty]
    public partial string TenantDetailsValueColumnHeader { get; set; }

    [ObservableProperty]
    public partial string TenantDetailsTenantIdLabel { get; set; }

    [ObservableProperty]
    public partial string TenantDetailsClientIdLabel { get; set; }

    [ObservableProperty]
    public partial string AppRegistrationLabel { get; set; }

    [ObservableProperty]
    public partial string AppRegistrationDescription { get; set; }

    [ObservableProperty]
    public partial string TenantOnboardingStatusLabel { get; set; }

    [ObservableProperty]
    public partial string TenantOnboardingStatusDescription { get; set; }

    [ObservableProperty]
    public partial string CertificateStatusLabel { get; set; }

    [ObservableProperty]
    public partial string CertificateStatusDescription { get; set; }

    [ObservableProperty]
    public partial string CertificateExpiredWarningText { get; set; }

    [ObservableProperty]
    public partial string CertificateThumbprintColumnHeader { get; set; }

    [ObservableProperty]
    public partial string CertificateCreatedColumnHeader { get; set; }

    [ObservableProperty]
    public partial string CertificateExpiresColumnHeader { get; set; }

    [ObservableProperty]
    public partial string CertificateIdColumnHeader { get; set; }

    [ObservableProperty]
    public partial string BootMediaCertificateLabel { get; set; }

    [ObservableProperty]
    public partial string BootMediaCertificateDescription { get; set; }

    [ObservableProperty]
    public partial string BootMediaCertificatePfxPathLabel { get; set; }

    [ObservableProperty]
    public partial string BootMediaCertificatePasswordLabel { get; set; }

    [ObservableProperty]
    public partial string SelectBootMediaCertificateButtonText { get; set; }

    [ObservableProperty]
    public partial string DefaultGroupTagLabel { get; set; }

    [ObservableProperty]
    public partial string DefaultGroupTagDescription { get; set; }

    [ObservableProperty]
    public partial string DefaultGroupTagNoneOptionText { get; set; }

    [ObservableProperty]
    public partial string KnownGroupTagsLabel { get; set; }

    [ObservableProperty]
    public partial string KnownGroupTagsDescription { get; set; }

    [ObservableProperty]
    public partial string AvailableGroupTagColumnHeader { get; set; }

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
    [NotifyCanExecuteChangedFor(nameof(SelectBootMediaCertificatePfxCommand))]
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
    [NotifyCanExecuteChangedFor(nameof(SelectBootMediaCertificatePfxCommand))]
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
    [NotifyCanExecuteChangedFor(nameof(SelectBootMediaCertificatePfxCommand))]
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
    [NotifyCanExecuteChangedFor(nameof(SelectBootMediaCertificatePfxCommand))]
    public partial bool IsConnectingTenant { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateCertificateCommand))]
    public partial CertificateValidityOptionViewModel? SelectedCertificateValidityOption { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RetireActiveCertificateCommand))]
    public partial AutopilotCertificateEntryViewModel? SelectedCertificate { get; set; }

    [ObservableProperty]
    public partial AutopilotGroupTagEntryViewModel? SelectedDefaultGroupTag { get; set; }

    /// <summary>
    /// Releases subscriptions to localization, configuration state, and profile collections.
    /// </summary>
    public void Dispose()
    {
        localizationService.LanguageChanged -= OnLanguageChanged;
        configurationStateService.StateChanged -= OnConfigurationStateChanged;
        Profiles.CollectionChanged -= OnProfilesCollectionChanged;
        SelectedProfiles.CollectionChanged -= OnSelectedProfilesCollectionChanged;
        SelectedCertificates.CollectionChanged -= OnSelectedCertificatesCollectionChanged;
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
            IReadOnlyList<AutopilotProfileSettings>? availableProfiles = await tenantOperationDialogService.RunAsync(
                localizationService.GetString("Autopilot.TenantDownloadDialogTitle"),
                localizationService.GetString("Autopilot.TenantDownloadDialogMessage"),
                autopilotTenantProfileService.DownloadFromTenantAsync);
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
        if (HasConnectedTenantInCurrentSession)
        {
            DisconnectTenantSession();
            return;
        }

        IsConnectingTenant = true;
        try
        {
            logger.Information("Starting Autopilot hardware hash tenant onboarding.");
            AutopilotTenantOnboardingResult? result = await tenantOperationDialogService.RunAsync(
                localizationService.GetString("Autopilot.HardwareHashTenantConnectionDialogTitle"),
                localizationService.GetString("Autopilot.HardwareHashTenantConnectionDialogMessage"),
                cancellationToken => autopilotTenantOnboardingService.ConnectAsync(hardwareHashUploadSettings, cancellationToken));
            if (result is null)
            {
                logger.Information("Autopilot hardware hash tenant onboarding was canceled.");
                return;
            }

            hardwareHashUploadSettings = result.Settings;
            tenantOnboardingStatus = result.Status;
            hardwareHashSessionState.TenantOnboardingStatus = result.Status;
            HasConnectedTenantInCurrentSession = true;
            ReplaceCertificates(result.Certificates);
            ReplaceAvailableGroupTags(result.Settings.KnownGroupTags, result.Settings.DefaultGroupTag);
            ClearBootMediaCertificateIfActiveCertificateChanged();
            RefreshHardwareHashUploadState();
            SaveState();
            logger.Information(
                "Autopilot hardware hash tenant onboarding updated. Status={Status}, TenantId={TenantId}, ApplicationObjectId={ApplicationObjectId}",
                result.Status,
                result.Settings.Tenant.TenantId,
                result.Settings.Tenant.ApplicationObjectId);
            if (ShouldShowTenantOnboardingResultDialog(result.Status))
            {
                await dialogService.ShowMessageAsync(new DialogRequest(
                    GetTenantOnboardingDialogTitle(result.Status),
                    result.Message));
            }
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
            tenantOnboardingStatus = AutopilotTenantOnboardingStatus.Ready;
            hardwareHashSessionState.TenantOnboardingStatus = tenantOnboardingStatus;
            ReplaceCertificates(result.Certificates);
            SetBootMediaCertificateInput(outputPath, result.GeneratedPassword);
            RefreshHardwareHashUploadState();
            SaveState();

            await certificateDialogService.ShowCreatedAsync(outputPath, result.GeneratedPassword);
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
        AutopilotCertificateEntryViewModel[] certificatesToRemove = SelectedCertificates.ToArray();
        if (certificatesToRemove.Length == 0)
        {
            return;
        }

        bool confirmed = await dialogService.ConfirmAsync(new ConfirmationDialogRequest(
            localizationService.GetString("Autopilot.HardwareHashRetireCertificateConfirmationTitle"),
            certificatesToRemove.Length == 1
                ? localizationService.GetString("Autopilot.HardwareHashRetireCertificateConfirmationMessage")
                : localizationService.FormatString("Autopilot.HardwareHashRetireCertificatesConfirmationMessageFormat", certificatesToRemove.Length),
            localizationService.GetString("Autopilot.HardwareHashRetireCertificateConfirmationPrimary"),
            localizationService.GetString("Common.Cancel")));
        if (!confirmed)
        {
            return;
        }

        IsConnectingTenant = true;
        try
        {
            AutopilotCertificateRemovalResult? result = null;
            foreach (AutopilotCertificateEntryViewModel certificate in certificatesToRemove)
            {
                result = await autopilotTenantOnboardingService.RemoveCertificateAsync(
                    hardwareHashUploadSettings,
                    certificate.KeyId);
                hardwareHashUploadSettings = result.Settings;
            }

            if (result is null)
            {
                return;
            }

            if (hardwareHashUploadSettings.ActiveCertificate is null)
            {
                tenantOnboardingStatus = AutopilotTenantOnboardingStatus.ActiveCertificateMissing;
                hardwareHashSessionState.TenantOnboardingStatus = tenantOnboardingStatus;
            }

            ReplaceCertificates(result.Certificates);
            ClearBootMediaCertificateIfActiveCertificateChanged();
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
        SelectBootMediaCertificatePfxCommand.NotifyCanExecuteChanged();
        SaveState();
    }

    [RelayCommand(CanExecute = nameof(CanSelectBootMediaCertificatePfx))]
    private async Task SelectBootMediaCertificatePfxAsync()
    {
        string? filePath = await filePickerService.PickOpenFileAsync(
            new FileOpenPickerRequest(localizationService.GetString("Autopilot.HardwareHashBootMediaCertificatePfxPickerTitle"), [".pfx"]));
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return;
        }

        SetBootMediaCertificateInput(filePath, hardwareHashUploadSettings.BootMediaCertificate.PfxPassword);
        RefreshHardwareHashUploadState();
        SaveState();
    }

    /// <summary>
    /// Updates the session-only boot media PFX password after the password box changes.
    /// </summary>
    /// <param name="password">PFX password entered by the operator.</param>
    public void SetBootMediaCertificatePassword(string password)
    {
        if (string.Equals(hardwareHashUploadSettings.BootMediaCertificate.PfxPassword, password, StringComparison.Ordinal))
        {
            return;
        }

        SetBootMediaCertificateInput(hardwareHashUploadSettings.BootMediaCertificate.PfxPath, password);
        RefreshBootMediaCertificateState();
        SaveState();
    }

    /// <summary>
    /// Gets the session-only boot media PFX password for synchronizing the password box.
    /// </summary>
    /// <returns>The current session PFX password, or an empty string.</returns>
    public string GetBootMediaCertificatePassword()
    {
        return hardwareHashUploadSettings.BootMediaCertificate.PfxPassword ?? string.Empty;
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
            hardwareHashUploadSettings = (settings.HardwareHashUpload ?? new AutopilotHardwareHashUploadSettings()) with
            {
                BootMediaCertificate = hardwareHashSessionState.BootMediaCertificate
            };
            tenantOnboardingStatus = hardwareHashSessionState.TenantOnboardingStatus;
            ReplaceCertificates(HasConnectedTenantInCurrentSession ? hardwareHashSessionState.Certificates : []);
            ReplaceAvailableGroupTags(hardwareHashUploadSettings.KnownGroupTags, hardwareHashUploadSettings.DefaultGroupTag);
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
        ActionsDescription = localizationService.GetString("Autopilot.ActionsDescription");
        DefaultProfileDescription = localizationService.GetString("Autopilot.DefaultProfileDescription");
        ProfilesDescription = localizationService.GetString("Autopilot.ProfilesDescription");
        HardwareHashHeader = localizationService.GetString("Autopilot.HardwareHashHeader");
        HardwareHashDescription = localizationService.GetString("Autopilot.HardwareHashDescription");
        HardwareHashEnableText = localizationService.GetString("Autopilot.HardwareHashEnableLabel");
        ConnectTenantButtonText = localizationService.GetString("Autopilot.HardwareHashConnectTenantButton");
        DisconnectTenantButtonText = localizationService.GetString("Autopilot.HardwareHashDisconnectTenantButton");
        ConnectingTenantStatusText = localizationService.GetString("Autopilot.HardwareHashConnectingTenantStatus");
        CertificateValidityLabel = localizationService.GetString("Autopilot.HardwareHashCertificateValidityLabel");
        CreateCertificateButtonText = localizationService.GetString("Autopilot.HardwareHashCreateCertificateButton");
        RetireCertificateButtonText = localizationService.GetString("Autopilot.HardwareHashRetireCertificateButton");
        TenantStatusLabel = localizationService.GetString("Autopilot.HardwareHashTenantStatusLabel");
        TenantStatusDescription = localizationService.GetString("Autopilot.HardwareHashTenantStatusDescription");
        TenantDetailsLabel = localizationService.GetString("Autopilot.HardwareHashTenantDetailsLabel");
        TenantDetailsDescription = localizationService.GetString("Autopilot.HardwareHashTenantDetailsDescription");
        TenantDetailsNameColumnHeader = localizationService.GetString("Autopilot.HardwareHashTenantDetailsNameColumn");
        TenantDetailsValueColumnHeader = localizationService.GetString("Autopilot.HardwareHashTenantDetailsValueColumn");
        TenantDetailsTenantIdLabel = localizationService.GetString("Autopilot.HardwareHashTenantDetailsTenantId");
        TenantDetailsClientIdLabel = localizationService.GetString("Autopilot.HardwareHashTenantDetailsClientId");
        AppRegistrationLabel = localizationService.GetString("Autopilot.HardwareHashAppRegistrationLabel");
        AppRegistrationDescription = localizationService.GetString("Autopilot.HardwareHashAppRegistrationDescription");
        TenantOnboardingStatusLabel = localizationService.GetString("Autopilot.HardwareHashOnboardingStatusLabel");
        TenantOnboardingStatusDescription = localizationService.GetString("Autopilot.HardwareHashOnboardingStatusDescription");
        CertificateStatusLabel = localizationService.GetString("Autopilot.HardwareHashCertificateStatusLabel");
        CertificateStatusDescription = localizationService.GetString("Autopilot.HardwareHashCertificateStatusDescription");
        CertificateExpiredWarningText = localizationService.GetString("Autopilot.HardwareHashCertificateExpiredWarning");
        CertificateThumbprintColumnHeader = localizationService.GetString("Autopilot.HardwareHashCertificateThumbprintColumn");
        CertificateCreatedColumnHeader = localizationService.GetString("Autopilot.HardwareHashCertificateCreatedColumn");
        CertificateExpiresColumnHeader = localizationService.GetString("Autopilot.HardwareHashCertificateExpiresColumn");
        CertificateIdColumnHeader = localizationService.GetString("Autopilot.HardwareHashCertificateIdColumn");
        BootMediaCertificateLabel = localizationService.GetString("Autopilot.HardwareHashBootMediaCertificateLabel");
        BootMediaCertificateDescription = localizationService.GetString("Autopilot.HardwareHashBootMediaCertificateDescription");
        BootMediaCertificatePfxPathLabel = localizationService.GetString("Autopilot.HardwareHashBootMediaCertificatePfxPathLabel");
        BootMediaCertificatePasswordLabel = localizationService.GetString("Autopilot.HardwareHashBootMediaCertificatePasswordLabel");
        SelectBootMediaCertificateButtonText = localizationService.GetString("Autopilot.HardwareHashBootMediaCertificateSelectButton");
        DefaultGroupTagLabel = localizationService.GetString("Autopilot.HardwareHashDefaultGroupTagLabel");
        DefaultGroupTagDescription = localizationService.GetString("Autopilot.HardwareHashDefaultGroupTagDescription");
        DefaultGroupTagNoneOptionText = localizationService.GetString("Autopilot.HardwareHashDefaultGroupTagNoneOption");
        KnownGroupTagsLabel = localizationService.GetString("Autopilot.HardwareHashKnownGroupTagsLabel");
        KnownGroupTagsDescription = localizationService.GetString("Autopilot.HardwareHashKnownGroupTagsDescription");
        AvailableGroupTagColumnHeader = localizationService.GetString("Autopilot.HardwareHashAvailableGroupTagColumn");
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
        OnPropertyChanged(nameof(TenantConnectionButtonText));
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
        RefreshTenantDetails();
        OnPropertyChanged(nameof(TenantStatusText));
        OnPropertyChanged(nameof(TenantConnectionButtonText));
        OnPropertyChanged(nameof(AppRegistrationStatusText));
        OnPropertyChanged(nameof(TenantOnboardingStatusText));
        OnPropertyChanged(nameof(TenantOnboardingStatusForeground));
        OnPropertyChanged(nameof(CertificateStatusText));
        OnPropertyChanged(nameof(CertificateStatusVisibility));
        OnPropertyChanged(nameof(CertificateStatusForeground));
        OnPropertyChanged(nameof(IsHardwareHashCertificateExpired));
        OnPropertyChanged(nameof(HardwareHashCertificateWarningVisibility));
        OnPropertyChanged(nameof(DefaultGroupTagText));
        OnPropertyChanged(nameof(DefaultGroupTagOptions));
        OnPropertyChanged(nameof(KnownGroupTagsText));
        OnPropertyChanged(nameof(HasAvailableGroupTags));
        OnPropertyChanged(nameof(AvailableGroupTagsVisibility));
        OnPropertyChanged(nameof(EmptyAvailableGroupTagsVisibility));
        OnPropertyChanged(nameof(ConnectedTenantDetailsVisibility));
        OnPropertyChanged(nameof(BootMediaCertificateVisibility));
        OnPropertyChanged(nameof(BootMediaCertificatePfxPath));
        OnPropertyChanged(nameof(BootMediaCertificateStatusText));
        OnPropertyChanged(nameof(BootMediaCertificateStatusForeground));
        OnPropertyChanged(nameof(IsBootMediaCertificateReady));
        RetireActiveCertificateCommand.NotifyCanExecuteChanged();
        SelectBootMediaCertificatePfxCommand.NotifyCanExecuteChanged();
    }

    private void RefreshBootMediaCertificateState()
    {
        OnPropertyChanged(nameof(BootMediaCertificatePfxPath));
        OnPropertyChanged(nameof(BootMediaCertificateStatusText));
        OnPropertyChanged(nameof(BootMediaCertificateStatusForeground));
        OnPropertyChanged(nameof(IsBootMediaCertificateReady));
        SelectBootMediaCertificatePfxCommand.NotifyCanExecuteChanged();
    }

    private void RefreshTenantDetails()
    {
        TenantDetails.Clear();
        if (!HasTenantRegistration)
        {
            return;
        }

        TenantDetails.Add(new AutopilotTenantDetailEntryViewModel(
            TenantDetailsTenantIdLabel,
            hardwareHashUploadSettings.Tenant.TenantId!));
        TenantDetails.Add(new AutopilotTenantDetailEntryViewModel(
            TenantDetailsClientIdLabel,
            hardwareHashUploadSettings.Tenant.ClientId!));
    }

    private void DisconnectTenantSession()
    {
        hardwareHashGraphSessionService.Disconnect();
        HasConnectedTenantInCurrentSession = false;
        tenantOnboardingStatus = null;
        ReplaceCertificates([]);
        ClearBootMediaCertificateInput();
        hardwareHashSessionState.ClearTenantConnection();
        RefreshHardwareHashUploadState();
        SaveState();
    }

    private void ReplaceAvailableGroupTags(IReadOnlyList<string> groupTags, string? preferredDefaultGroupTag)
    {
        string[] orderedGroupTags = groupTags
            .Where(groupTag => !string.IsNullOrWhiteSpace(groupTag))
            .Select(groupTag => groupTag.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(groupTag => groupTag, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        AvailableGroupTags.Clear();
        DefaultGroupTagOptions.Clear();
        DefaultGroupTagOptions.Add(new AutopilotGroupTagEntryViewModel(DefaultGroupTagNoneOptionText, null));
        foreach (string groupTag in orderedGroupTags)
        {
            AutopilotGroupTagEntryViewModel groupTagEntry = new(groupTag, groupTag);
            AvailableGroupTags.Add(groupTagEntry);
            DefaultGroupTagOptions.Add(groupTagEntry);
        }

        AutopilotGroupTagEntryViewModel? selectedGroupTag = DefaultGroupTagOptions.FirstOrDefault(groupTag =>
                                                       string.Equals(groupTag.GroupTag, preferredDefaultGroupTag, StringComparison.OrdinalIgnoreCase))
                                                   ?? DefaultGroupTagOptions.First();
        SelectedDefaultGroupTag = selectedGroupTag;
        hardwareHashUploadSettings = hardwareHashUploadSettings with
        {
            KnownGroupTags = orderedGroupTags,
            DefaultGroupTag = selectedGroupTag?.GroupTag
        };
    }

    private void ReplaceCertificates(IReadOnlyList<AutopilotGraphKeyCredential> credentials)
    {
        Certificates.Clear();
        SelectedCertificates.Clear();
        foreach (AutopilotCertificateEntryViewModel certificate in credentials
                     .OrderBy(certificate => certificate.ExpiresOnUtc)
                     .Select(AutopilotCertificateEntryViewModel.FromGraphCredential))
        {
            Certificates.Add(certificate);
        }

        SelectedCertificate = null;
        hardwareHashSessionState.Certificates = credentials;
        OnPropertyChanged(nameof(HasCertificates));
        OnPropertyChanged(nameof(CertificatesVisibility));
        OnPropertyChanged(nameof(BootMediaCertificateVisibility));
    }

    private string CreateCertificateStatusText()
    {
        AutopilotCertificateMetadata? certificate = hardwareHashUploadSettings.ActiveCertificate;
        if (certificate is null)
        {
            return HasCertificates
                ? string.Empty
                : localizationService.GetString("Autopilot.HardwareHashCertificateMissing");
        }

        if (certificate.ExpiresOnUtc is null)
        {
            return localizationService.GetString("Autopilot.HardwareHashCertificateExpirationMissing");
        }

        return certificate.ExpiresOnUtc <= DateTimeOffset.UtcNow
            ? localizationService.FormatString("Autopilot.HardwareHashCertificateExpiredFormat", certificate.ExpiresOnUtc.Value.LocalDateTime)
            : string.Empty;
    }

    private Brush ResolveCertificateStatusBrush()
    {
        AutopilotCertificateMetadata? certificate = hardwareHashUploadSettings.ActiveCertificate;
        if (certificate?.ExpiresOnUtc is not DateTimeOffset expiresOnUtc)
        {
            return (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];
        }

        if (expiresOnUtc <= DateTimeOffset.UtcNow)
        {
            return (Brush)Application.Current.Resources["SystemFillColorCriticalBrush"];
        }

        return expiresOnUtc - DateTimeOffset.UtcNow <= TimeSpan.FromDays(30)
            ? (Brush)Application.Current.Resources["SystemFillColorCautionBrush"]
            : (Brush)Application.Current.Resources["SystemFillColorSuccessBrush"];
    }

    private string CreateBootMediaCertificateStatusText()
    {
        if (string.IsNullOrWhiteSpace(hardwareHashUploadSettings.BootMediaCertificate.PfxPath))
        {
            return localizationService.GetString("Autopilot.HardwareHashBootMediaCertificatePfxMissing");
        }

        if (isBootMediaCertificateFileMissing)
        {
            return localizationService.GetString("Autopilot.HardwareHashBootMediaCertificateFileMissing");
        }

        if (bootMediaCertificateValidationCode == AutopilotPfxValidationCode.PasswordRequired)
        {
            return localizationService.GetString("Autopilot.HardwareHashBootMediaCertificatePasswordMissing");
        }

        if (hardwareHashUploadSettings.ActiveCertificate is null)
        {
            return bootMediaCertificateValidationCode == AutopilotPfxValidationCode.ThumbprintMismatch
                ? localizationService.GetString("Autopilot.HardwareHashBootMediaCertificateThumbprintMismatch")
                : localizationService.GetString("Autopilot.HardwareHashBootMediaCertificateActiveMissing");
        }

        if (IsHardwareHashCertificateExpired)
        {
            return localizationService.GetString("Autopilot.HardwareHashBootMediaCertificateActiveExpired");
        }

        if (IsBootMediaCertificateReady)
        {
            return localizationService.GetString("Autopilot.HardwareHashBootMediaCertificateReady");
        }

        return bootMediaCertificateValidationCode switch
        {
            AutopilotPfxValidationCode.Valid => localizationService.GetString("Autopilot.HardwareHashBootMediaCertificateReady"),
            AutopilotPfxValidationCode.PasswordRequired => localizationService.GetString("Autopilot.HardwareHashBootMediaCertificatePasswordMissing"),
            AutopilotPfxValidationCode.ThumbprintMismatch => localizationService.GetString("Autopilot.HardwareHashBootMediaCertificateThumbprintMismatch"),
            AutopilotPfxValidationCode.PrivateKeyMissing => localizationService.GetString("Autopilot.HardwareHashBootMediaCertificatePrivateKeyMissing"),
            AutopilotPfxValidationCode.ExpectedThumbprintRequired => localizationService.GetString("Autopilot.HardwareHashBootMediaCertificateActiveMissing"),
            AutopilotPfxValidationCode.InvalidPfx => localizationService.GetString("Autopilot.HardwareHashBootMediaCertificateInvalidPfx"),
            _ => localizationService.GetString("Autopilot.HardwareHashBootMediaCertificatePfxMissing")
        };
    }

    private Brush ResolveBootMediaCertificateStatusBrush()
    {
        if (IsBootMediaCertificateReady)
        {
            return (Brush)Application.Current.Resources["SystemFillColorSuccessBrush"];
        }

        if (IsHardwareHashCertificateExpired ||
            isBootMediaCertificateFileMissing ||
            (hardwareHashUploadSettings.ActiveCertificate is null &&
             !string.IsNullOrWhiteSpace(hardwareHashUploadSettings.BootMediaCertificate.PfxPath) &&
             bootMediaCertificateValidationCode != AutopilotPfxValidationCode.PasswordRequired) ||
            bootMediaCertificateValidationCode is AutopilotPfxValidationCode.InvalidPfx
                or AutopilotPfxValidationCode.PrivateKeyMissing
                or AutopilotPfxValidationCode.ThumbprintMismatch)
        {
            return (Brush)Application.Current.Resources["SystemFillColorCriticalBrush"];
        }

        return (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];
    }

    private void SetBootMediaCertificateInput(string? pfxPath, string? password)
    {
        string? normalizedPath = string.IsNullOrWhiteSpace(pfxPath) ? null : pfxPath.Trim();
        AutopilotBootMediaCertificateSettings bootMediaCertificate = new()
        {
            PfxPath = normalizedPath,
            PfxPassword = password
        };

        isBootMediaCertificateFileMissing = false;
        bootMediaCertificateValidationCode = AutopilotPfxValidationCode.PfxRequired;
        if (!string.IsNullOrWhiteSpace(normalizedPath))
        {
            if (File.Exists(normalizedPath))
            {
                try
                {
                    AutopilotPfxValidationResult validation = AutopilotPfxCertificateValidator.Validate(
                        File.ReadAllBytes(normalizedPath),
                        password);
                    bootMediaCertificateValidationCode = validation.Code;
                    if (validation.IsValid)
                    {
                        AutopilotCertificateEntryViewModel? matchingCertificate = FindTenantCertificateByThumbprint(validation.Thumbprint);
                        if (matchingCertificate is null)
                        {
                            bootMediaCertificateValidationCode = AutopilotPfxValidationCode.ThumbprintMismatch;
                            hardwareHashUploadSettings = hardwareHashUploadSettings with { ActiveCertificate = null };
                        }
                        else
                        {
                            hardwareHashUploadSettings = hardwareHashUploadSettings with
                            {
                                ActiveCertificate = CreateCertificateMetadata(matchingCertificate)
                            };
                            tenantOnboardingStatus = ResolveCertificateSelectionStatus(matchingCertificate);
                            hardwareHashSessionState.TenantOnboardingStatus = tenantOnboardingStatus;
                            bootMediaCertificate = bootMediaCertificate with
                            {
                                ValidatedThumbprint = validation.Thumbprint,
                                ValidatedExpiresOnUtc = validation.ExpiresOnUtc
                            };
                        }
                    }
                    else
                    {
                        hardwareHashUploadSettings = hardwareHashUploadSettings with { ActiveCertificate = null };
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    bootMediaCertificateValidationCode = AutopilotPfxValidationCode.InvalidPfx;
                    hardwareHashUploadSettings = hardwareHashUploadSettings with { ActiveCertificate = null };
                }
            }
            else
            {
                isBootMediaCertificateFileMissing = true;
                hardwareHashUploadSettings = hardwareHashUploadSettings with { ActiveCertificate = null };
            }
        }

        hardwareHashUploadSettings = hardwareHashUploadSettings with
        {
            BootMediaCertificate = bootMediaCertificate
        };
        hardwareHashSessionState.BootMediaCertificate = bootMediaCertificate;
    }

    private void ClearBootMediaCertificateIfActiveCertificateChanged()
    {
        string? activeThumbprint = NormalizeThumbprint(hardwareHashUploadSettings.ActiveCertificate?.Thumbprint);
        string? validatedThumbprint = NormalizeThumbprint(hardwareHashUploadSettings.BootMediaCertificate.ValidatedThumbprint);
        if (string.Equals(activeThumbprint, validatedThumbprint, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        ClearBootMediaCertificateInput();
    }

    private void ClearBootMediaCertificateInput()
    {
        bootMediaCertificateValidationCode = AutopilotPfxValidationCode.PfxRequired;
        isBootMediaCertificateFileMissing = false;
        hardwareHashUploadSettings = hardwareHashUploadSettings with
        {
            BootMediaCertificate = new AutopilotBootMediaCertificateSettings()
        };
        hardwareHashSessionState.BootMediaCertificate = hardwareHashUploadSettings.BootMediaCertificate;
        OnPropertyChanged(nameof(BootMediaCertificatePfxPath));
    }

    private string CreateTenantOnboardingStatusText()
    {
        return tenantOnboardingStatus == AutopilotTenantOnboardingStatus.Ready
            ? localizationService.GetString("Autopilot.HardwareHashOnboardingStatusReady")
            : localizationService.GetString("Autopilot.HardwareHashOnboardingStatusNotReady");
    }

    private Brush ResolveTenantOnboardingStatusBrush()
    {
        return tenantOnboardingStatus == AutopilotTenantOnboardingStatus.Ready
            ? (Brush)Application.Current.Resources["SystemFillColorSuccessBrush"]
            : (Brush)Application.Current.Resources["SystemFillColorCriticalBrush"];
    }

    private string GetTenantOnboardingDialogTitle(AutopilotTenantOnboardingStatus status)
    {
        return status == AutopilotTenantOnboardingStatus.Ready
            ? localizationService.GetString("Autopilot.HardwareHashOnboardingCompletedTitle")
            : localizationService.GetString("Autopilot.HardwareHashOnboardingRequiresAttentionTitle");
    }

    private static bool ShouldShowTenantOnboardingResultDialog(AutopilotTenantOnboardingStatus status)
    {
        return status is not AutopilotTenantOnboardingStatus.ActiveCertificateMissing;
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

    private void OnSelectedCertificatesCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        RetireActiveCertificateCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedDefaultGroupTagChanged(AutopilotGroupTagEntryViewModel? value)
    {
        if (isApplyingState)
        {
            return;
        }

        hardwareHashUploadSettings = hardwareHashUploadSettings with
        {
            DefaultGroupTag = value?.GroupTag
        };
        OnPropertyChanged(nameof(DefaultGroupTagText));
        SaveState();
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

    /// <summary>
    /// Replaces the selected certificate rows after a XAML selection change.
    /// </summary>
    /// <param name="certificates">The selected certificate view models.</param>
    public void ReplaceSelectedCertificate(IEnumerable<AutopilotCertificateEntryViewModel> certificates)
    {
        SelectedCertificates.Clear();
        foreach (AutopilotCertificateEntryViewModel certificate in certificates)
        {
            SelectedCertificates.Add(certificate);
        }

        SelectedCertificate = SelectedCertificates.FirstOrDefault();
    }

    private AutopilotTenantOnboardingStatus? ResolveCertificateSelectionStatus(AutopilotCertificateEntryViewModel? certificate)
    {
        if (tenantOnboardingStatus is AutopilotTenantOnboardingStatus.PermissionMissing
            or AutopilotTenantOnboardingStatus.ConsentMissing
            or AutopilotTenantOnboardingStatus.ServicePrincipalUnavailable
            or AutopilotTenantOnboardingStatus.AppRegistrationMissing
            or AutopilotTenantOnboardingStatus.AdoptionRequired)
        {
            return tenantOnboardingStatus;
        }

        if (certificate is null)
        {
            return AutopilotTenantOnboardingStatus.ActiveCertificateMissing;
        }

        return certificate.ExpiresOnUtc <= DateTimeOffset.UtcNow
            ? AutopilotTenantOnboardingStatus.ActiveCertificateExpired
            : AutopilotTenantOnboardingStatus.Ready;
    }

    private static AutopilotCertificateMetadata CreateCertificateMetadata(AutopilotCertificateEntryViewModel certificate)
    {
        return new AutopilotCertificateMetadata
        {
            KeyId = certificate.KeyId,
            Thumbprint = certificate.Thumbprint,
            DisplayName = AutopilotHardwareHashUploadSettings.ManagedAppRegistrationDisplayName,
            ExpiresOnUtc = certificate.ExpiresOnUtc
        };
    }

    private AutopilotCertificateEntryViewModel? FindTenantCertificateByThumbprint(string? thumbprint)
    {
        string? normalizedThumbprint = NormalizeThumbprint(thumbprint);
        if (string.IsNullOrWhiteSpace(normalizedThumbprint))
        {
            return null;
        }

        return Certificates.FirstOrDefault(certificate =>
            string.Equals(
                NormalizeThumbprint(certificate.Thumbprint),
                normalizedThumbprint,
                StringComparison.OrdinalIgnoreCase));
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
               HasConnectedTenantInCurrentSession &&
               !string.IsNullOrWhiteSpace(hardwareHashUploadSettings.Tenant.ApplicationObjectId);
    }

    private bool CanRetireActiveCertificate()
    {
        return IsAutopilotEnabled &&
               IsHardwareHashUploadMode &&
               !IsBusy &&
               HasConnectedTenantInCurrentSession &&
               !string.IsNullOrWhiteSpace(hardwareHashUploadSettings.Tenant.ApplicationObjectId) &&
               SelectedCertificates.Count > 0;
    }

    private bool CanSelectBootMediaCertificatePfx()
    {
        return IsAutopilotEnabled &&
               IsHardwareHashUploadMode &&
               !IsBusy &&
               HasConnectedTenantInCurrentSession &&
               HasCertificates;
    }

    private static string? NormalizeThumbprint(string? thumbprint)
    {
        string? normalized = thumbprint?.Replace(" ", string.Empty, StringComparison.Ordinal).Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized.ToUpperInvariant();
    }

    public sealed record CertificateValidityOptionViewModel(int Months, string DisplayName);
}
