using System.Collections.ObjectModel;
using System.Text.Json;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Application;
using Foundry.Core.Services.Configuration;
using Foundry.Services.Configuration;
using Foundry.Services.Localization;
using Microsoft.UI.Xaml;

namespace Foundry.ViewModels;

public sealed partial class AutopilotConfigurationViewModel : ObservableObject, IDisposable
{
    private readonly IExpertDeployConfigurationStateService configurationStateService;
    private readonly IAutopilotProfileImportService autopilotProfileImportService;
    private readonly IFilePickerService filePickerService;
    private readonly IDialogService dialogService;
    private readonly IApplicationLocalizationService localizationService;
    private bool isApplyingState = true;
    private bool isSavingState;

    public AutopilotConfigurationViewModel(
        IExpertDeployConfigurationStateService configurationStateService,
        IAutopilotProfileImportService autopilotProfileImportService,
        IFilePickerService filePickerService,
        IDialogService dialogService,
        IApplicationLocalizationService localizationService)
    {
        this.configurationStateService = configurationStateService;
        this.autopilotProfileImportService = autopilotProfileImportService;
        this.filePickerService = filePickerService;
        this.dialogService = dialogService;
        this.localizationService = localizationService;

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
    public partial string StorageLabel { get; set; }

    [ObservableProperty]
    public partial string InjectionLabel { get; set; }

    [ObservableProperty]
    public partial string BootImageStoragePath { get; set; }

    [ObservableProperty]
    public partial string OfflineInjectionPath { get; set; }

    [ObservableProperty]
    public partial string ImportButtonText { get; set; }

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
    [NotifyCanExecuteChangedFor(nameof(RemoveSelectedProfileCommand))]
    public partial bool IsAutopilotEnabled { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RemoveSelectedProfileCommand))]
    public partial AutopilotProfileEntryViewModel? SelectedProfile { get; set; }

    [ObservableProperty]
    public partial AutopilotProfileEntryViewModel? SelectedDefaultProfile { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ImportProfileCommand))]
    [NotifyCanExecuteChangedFor(nameof(RemoveSelectedProfileCommand))]
    public partial bool IsImporting { get; set; }

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
        StorageLabel = localizationService.GetString("Autopilot.StorageLabel");
        InjectionLabel = localizationService.GetString("Autopilot.InjectionLabel");
        BootImageStoragePath = @"X:\Foundry\Config\Autopilot";
        OfflineInjectionPath = @"%SystemDrive%\Windows\Provisioning\Autopilot\AutopilotConfigurationFile.json";
        ImportButtonText = localizationService.GetString("Autopilot.ImportButton");
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
        return IsAutopilotEnabled && !IsImporting;
    }

    private bool CanRemoveSelectedProfile()
    {
        return IsAutopilotEnabled && !IsImporting && SelectedProfile is not null;
    }
}
