using System.Collections.ObjectModel;
using System.Text.Json;
using Azure.Identity;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Application;
using Foundry.Core.Services.Configuration;
using Foundry.Services.Autopilot;
using Foundry.Services.Configuration;
using Foundry.Services.Localization;
using Microsoft.UI.Xaml;
using Serilog;

namespace Foundry.ViewModels;

public sealed partial class AutopilotConfigurationViewModel : ObservableObject, IDisposable
{
    private readonly IExpertDeployConfigurationStateService configurationStateService;
    private readonly IAutopilotProfileImportService autopilotProfileImportService;
    private readonly IAutopilotTenantProfileService autopilotTenantProfileService;
    private readonly IAutopilotTenantDownloadDialogService tenantDownloadDialogService;
    private readonly IAutopilotProfileSelectionDialogService profileSelectionDialogService;
    private readonly IFilePickerService filePickerService;
    private readonly IDialogService dialogService;
    private readonly IApplicationLocalizationService localizationService;
    private readonly ILogger logger;
    private bool isApplyingState = true;
    private bool isSavingState;

    public AutopilotConfigurationViewModel(
        IExpertDeployConfigurationStateService configurationStateService,
        IAutopilotProfileImportService autopilotProfileImportService,
        IAutopilotTenantProfileService autopilotTenantProfileService,
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
    }

    public ObservableCollection<AutopilotProfileEntryViewModel> Profiles { get; } = [];
    public ObservableCollection<AutopilotProfileEntryViewModel> SelectedProfiles { get; } = [];

    public bool IsAutopilotSectionEnabled => IsAutopilotEnabled;
    public bool HasProfiles => Profiles.Count > 0;
    public Visibility EmptyProfilesVisibility => HasProfiles ? Visibility.Collapsed : Visibility.Visible;
    public Visibility ProfilesVisibility => HasProfiles ? Visibility.Visible : Visibility.Collapsed;
    public bool IsBusy => IsImporting || IsDownloading;
    public Visibility BusyStatusVisibility => IsBusy ? Visibility.Visible : Visibility.Collapsed;
    public string BusyStatusText => IsImporting
        ? ImportingStatusText
        : IsDownloading
            ? DownloadingStatusText
            : string.Empty;

    [ObservableProperty]
    public partial string PageTitle { get; set; }

    [ObservableProperty]
    public partial string AutopilotHeader { get; set; }

    [ObservableProperty]
    public partial string AutopilotDescription { get; set; }

    [ObservableProperty]
    public partial string EnableAutopilotText { get; set; }

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
    [NotifyCanExecuteChangedFor(nameof(ImportProfileCommand))]
    [NotifyCanExecuteChangedFor(nameof(DownloadProfilesCommand))]
    [NotifyCanExecuteChangedFor(nameof(RemoveSelectedProfilesCommand))]
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
    public partial bool IsImporting { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ImportProfileCommand))]
    [NotifyCanExecuteChangedFor(nameof(DownloadProfilesCommand))]
    [NotifyCanExecuteChangedFor(nameof(RemoveSelectedProfilesCommand))]
    [NotifyPropertyChangedFor(nameof(IsBusy))]
    [NotifyPropertyChangedFor(nameof(BusyStatusText))]
    [NotifyPropertyChangedFor(nameof(BusyStatusVisibility))]
    public partial bool IsDownloading { get; set; }

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

    partial void OnIsAutopilotEnabledChanged(bool value)
    {
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
            ReplaceProfiles(
                settings.Profiles.Select(AutopilotProfileEntryViewModel.FromSettings),
                settings.DefaultProfileId);
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
                DefaultProfileId = SelectedDefaultProfile?.Id,
                Profiles = Profiles.Select(profile => profile.ToSettings()).ToArray()
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
        return IsAutopilotEnabled && !IsImporting && !IsDownloading;
    }

    private bool CanDownloadProfiles()
    {
        return IsAutopilotEnabled && !IsImporting && !IsDownloading;
    }

    private bool CanRemoveSelectedProfiles()
    {
        return IsAutopilotEnabled && !IsImporting && !IsDownloading && SelectedProfiles.Count > 0;
    }
}
