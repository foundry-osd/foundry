using System.Collections.ObjectModel;
using System.Text.Json;
using Azure.Identity;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Application;
using Foundry.Core.Services.Configuration;
using Foundry.Services.Autopilot;
using Foundry.Services.Configuration;
using Foundry.Services.Localization;
using Foundry.Services.Operations;
using Foundry.Services.Shell;
using Microsoft.UI.Xaml;
using Serilog;

namespace Foundry.ViewModels;

public sealed partial class AutopilotConfigurationViewModel : ObservableObject, IDisposable
{
    private readonly IExpertDeployConfigurationStateService configurationStateService;
    private readonly IAutopilotProfileImportService autopilotProfileImportService;
    private readonly IAutopilotTenantProfileService autopilotTenantProfileService;
    private readonly IAutopilotProfileSelectionDialogService profileSelectionDialogService;
    private readonly IFilePickerService filePickerService;
    private readonly IDialogService dialogService;
    private readonly IOperationProgressService operationProgressService;
    private readonly IShellNavigationGuardService shellNavigationGuardService;
    private readonly IApplicationLocalizationService localizationService;
    private readonly ILogger logger;
    private bool isApplyingState = true;
    private bool isSavingState;

    public AutopilotConfigurationViewModel(
        IExpertDeployConfigurationStateService configurationStateService,
        IAutopilotProfileImportService autopilotProfileImportService,
        IAutopilotTenantProfileService autopilotTenantProfileService,
        IAutopilotProfileSelectionDialogService profileSelectionDialogService,
        IFilePickerService filePickerService,
        IDialogService dialogService,
        IOperationProgressService operationProgressService,
        IShellNavigationGuardService shellNavigationGuardService,
        IApplicationLocalizationService localizationService,
        ILogger logger)
    {
        this.configurationStateService = configurationStateService;
        this.autopilotProfileImportService = autopilotProfileImportService;
        this.autopilotTenantProfileService = autopilotTenantProfileService;
        this.profileSelectionDialogService = profileSelectionDialogService;
        this.filePickerService = filePickerService;
        this.dialogService = dialogService;
        this.operationProgressService = operationProgressService;
        this.shellNavigationGuardService = shellNavigationGuardService;
        this.localizationService = localizationService;
        this.logger = logger.ForContext<AutopilotConfigurationViewModel>();

        RefreshLocalizedText();
        ApplyState(configurationStateService.Current.Autopilot);

        localizationService.LanguageChanged += OnLanguageChanged;
        configurationStateService.StateChanged += OnConfigurationStateChanged;
        Profiles.CollectionChanged += OnProfilesCollectionChanged;
        isApplyingState = false;
    }

    public ObservableCollection<AutopilotProfileEntryViewModel> Profiles { get; } = [];

    public bool IsAutopilotSectionEnabled => IsAutopilotEnabled;
    public bool HasProfiles => Profiles.Count > 0;
    public Visibility EmptyProfilesVisibility => HasProfiles ? Visibility.Collapsed : Visibility.Visible;
    public Visibility ProfilesVisibility => HasProfiles ? Visibility.Visible : Visibility.Collapsed;

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
    [NotifyCanExecuteChangedFor(nameof(RemoveSelectedProfileCommand))]
    public partial bool IsAutopilotEnabled { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RemoveSelectedProfileCommand))]
    public partial AutopilotProfileEntryViewModel? SelectedProfile { get; set; }

    [ObservableProperty]
    public partial AutopilotProfileEntryViewModel? SelectedDefaultProfile { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ImportProfileCommand))]
    [NotifyCanExecuteChangedFor(nameof(DownloadProfilesCommand))]
    [NotifyCanExecuteChangedFor(nameof(RemoveSelectedProfileCommand))]
    public partial bool IsImporting { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ImportProfileCommand))]
    [NotifyCanExecuteChangedFor(nameof(DownloadProfilesCommand))]
    [NotifyCanExecuteChangedFor(nameof(RemoveSelectedProfileCommand))]
    public partial bool IsDownloading { get; set; }

    public void Dispose()
    {
        localizationService.LanguageChanged -= OnLanguageChanged;
        configurationStateService.StateChanged -= OnConfigurationStateChanged;
        Profiles.CollectionChanged -= OnProfilesCollectionChanged;
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
        ShellNavigationState previousShellState = shellNavigationGuardService.State;
        string terminalStatus = localizationService.GetString("Autopilot.DownloadCanceled");
        shellNavigationGuardService.SetState(ShellNavigationState.OperationRunning);
        operationProgressService.Start(
            OperationKind.AutopilotProfileDownload,
            localizationService.GetString("Autopilot.DownloadInProgress"));

        IReadOnlyList<AutopilotProfileSettings> availableProfiles;
        try
        {
            logger.Information("Starting Autopilot profile download from tenant.");
            operationProgressService.Report(20, localizationService.GetString("Autopilot.DownloadConnecting"));
            availableProfiles = await autopilotTenantProfileService.DownloadFromTenantAsync();

            if (availableProfiles.Count == 0)
            {
                terminalStatus = localizationService.GetString("Autopilot.DownloadCompletedNoProfiles");
                operationProgressService.Complete(terminalStatus);
                logger.Information("Autopilot tenant download completed. ProfileCount=0");
                return;
            }

            terminalStatus = localizationService.GetString("Autopilot.DownloadSelectProfiles");
            operationProgressService.Report(70, terminalStatus);
        }
        catch (Exception ex) when (ex is OperationCanceledException or InvalidOperationException or HttpRequestException or JsonException or AuthenticationFailedException)
        {
            terminalStatus = localizationService.FormatString("Autopilot.DownloadFailedFormat", ex.Message);
            operationProgressService.Complete(terminalStatus);
            logger.Error(ex, "Autopilot tenant download failed.");
            await dialogService.ShowMessageAsync(new DialogRequest(
                localizationService.GetString("Autopilot.DownloadFailedTitle"),
                terminalStatus));
            return;
        }
        finally
        {
            operationProgressService.Reset(terminalStatus);
            shellNavigationGuardService.SetState(previousShellState == ShellNavigationState.OperationRunning
                ? ShellNavigationState.Ready
                : previousShellState);
            IsDownloading = false;
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

    [RelayCommand(CanExecute = nameof(CanRemoveSelectedProfile))]
    private void RemoveSelectedProfile()
    {
        if (SelectedProfile is null)
        {
            return;
        }

        string removedProfileId = SelectedProfile.Id;
        Profiles.Remove(SelectedProfile);

        SelectedProfile = Profiles.FirstOrDefault();
        if (SelectedDefaultProfile is null ||
            string.Equals(SelectedDefaultProfile.Id, removedProfileId, StringComparison.OrdinalIgnoreCase))
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
                settings.DefaultProfileId,
                SelectedProfile?.Id);
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
        ReplaceProfiles(mergedProfiles.Values, preferredDefaultProfileId, SelectedProfile?.Id);
        SaveState();
    }

    private void ReplaceProfiles(
        IEnumerable<AutopilotProfileEntryViewModel> profiles,
        string? preferredDefaultProfileId,
        string? preferredSelectedProfileId)
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

        SelectedProfile = Profiles.FirstOrDefault(profile =>
                              string.Equals(profile.Id, preferredSelectedProfileId, StringComparison.OrdinalIgnoreCase))
                          ?? Profiles.FirstOrDefault();

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
        DefaultProfileLabel = localizationService.GetString("Autopilot.DefaultProfileLabel");
        ProfilesLabel = localizationService.GetString("Autopilot.ProfilesLabel");
        EmptyProfilesText = localizationService.GetString("Autopilot.ProfilesEmptyLabel");
        ProfileNameColumnHeader = localizationService.GetString("Autopilot.ColumnName");
        ProfileSourceColumnHeader = localizationService.GetString("Autopilot.ColumnSource");
        ProfileImportedColumnHeader = localizationService.GetString("Autopilot.ColumnImported");
        ProfileFolderColumnHeader = localizationService.GetString("Autopilot.ColumnFolder");
    }

    private void RefreshProfileState()
    {
        OnPropertyChanged(nameof(HasProfiles));
        OnPropertyChanged(nameof(EmptyProfilesVisibility));
        OnPropertyChanged(nameof(ProfilesVisibility));
        RemoveSelectedProfileCommand.NotifyCanExecuteChanged();
    }

    private void OnProfilesCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        RefreshProfileState();
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

    private bool CanRemoveSelectedProfile()
    {
        return IsAutopilotEnabled && !IsImporting && !IsDownloading && SelectedProfile is not null;
    }
}
