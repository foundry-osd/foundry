using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Foundry.Models.Configuration;
using Foundry.Services.ApplicationShell;
using Foundry.Services.Autopilot;
using Foundry.Services.Localization;
using Foundry.Services.Operations;

namespace Foundry.ViewModels;

public partial class AutopilotSettingsViewModel : LocalizedViewModelBase
{
    private readonly IApplicationShellService _applicationShellService;
    private readonly IAutopilotProfileService _autopilotProfileService;
    private readonly IOperationProgressService _operationProgressService;

    public AutopilotSettingsViewModel(
        ILocalizationService localizationService,
        IApplicationShellService applicationShellService,
        IAutopilotProfileService autopilotProfileService,
        IOperationProgressService operationProgressService)
        : base(localizationService)
    {
        _applicationShellService = applicationShellService;
        _autopilotProfileService = autopilotProfileService;
        _operationProgressService = operationProgressService;
        _operationProgressService.ProgressChanged += OnOperationProgressChanged;
    }

    public ObservableCollection<AutopilotProfileEntry> Profiles { get; } = [];

    [ObservableProperty]
    private bool isAutopilotEnabled;

    [ObservableProperty]
    private AutopilotProfileEntry? selectedProfile;

    [ObservableProperty]
    private AutopilotProfileEntry? selectedDefaultProfile;

    public bool HasProfiles => Profiles.Count > 0;

    public string BootImageStoragePath => @"X:\Foundry\Config\Autopilot";

    public string OfflineInjectionPath => @"%SystemDrive%\Windows\Provisioning\Autopilot\AutopilotConfigurationFile.json";

    [RelayCommand(CanExecute = nameof(CanManageProfiles))]
    private async Task ImportProfileAsync()
    {
        string? filePath = _applicationShellService.PickOpenFilePath(
            Strings["AutopilotImportTitle"],
            Strings["JsonPickerFilter"]);
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return;
        }

        if (!_operationProgressService.TryStart(OperationKind.AutopilotProfileImport, Strings["AutopilotImportInProgress"], 0))
        {
            return;
        }

        try
        {
            _operationProgressService.Report(40, Strings["AutopilotImportValidating"]);
            AutopilotProfileSettings profile = await _autopilotProfileService
                .ImportFromJsonFileAsync(filePath)
                .ConfigureAwait(false);

            MergeProfiles([profile]);
            _operationProgressService.Complete(string.Format(Strings["AutopilotImportCompletedFormat"], profile.DisplayName));
        }
        catch (Exception ex)
        {
            _operationProgressService.Fail(string.Format(Strings["AutopilotImportFailedFormat"], ex.Message));
        }
    }

    [RelayCommand(CanExecute = nameof(CanManageProfiles))]
    private async Task DownloadProfilesAsync()
    {
        if (!_operationProgressService.TryStart(OperationKind.AutopilotProfileDownload, Strings["AutopilotDownloadInProgress"], 0))
        {
            return;
        }

        try
        {
            _operationProgressService.Report(20, Strings["AutopilotDownloadConnecting"]);
            IReadOnlyList<AutopilotProfileSettings> profiles = await _autopilotProfileService
                .DownloadFromTenantAsync()
                .ConfigureAwait(false);

            MergeProfiles(profiles);
            _operationProgressService.Complete(
                profiles.Count == 0
                    ? Strings["AutopilotDownloadCompletedNoProfiles"]
                    : string.Format(Strings["AutopilotDownloadCompletedFormat"], profiles.Count));
        }
        catch (Exception ex)
        {
            _operationProgressService.Fail(string.Format(Strings["AutopilotDownloadFailedFormat"], ex.Message));
        }
    }

    [RelayCommand(CanExecute = nameof(CanRemoveSelectedProfile))]
    private void RemoveSelectedProfile()
    {
        if (SelectedProfile is null)
        {
            return;
        }

        string removedId = SelectedProfile.Id;
        int index = Profiles.IndexOf(SelectedProfile);
        if (index >= 0)
        {
            Profiles.RemoveAt(index);
        }

        if (SelectedDefaultProfile?.Id == removedId)
        {
            SelectedDefaultProfile = Profiles.FirstOrDefault();
        }

        SelectedProfile = Profiles.FirstOrDefault();
        OnPropertyChanged(nameof(HasProfiles));
    }

    public AutopilotSettings BuildSettings()
    {
        return new AutopilotSettings
        {
            IsEnabled = IsAutopilotEnabled,
            DefaultProfileId = SelectedDefaultProfile?.Id,
            Profiles = Profiles.Select(profile => profile.ToSettings()).ToArray()
        };
    }

    public void ApplySettings(AutopilotSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        IsAutopilotEnabled = settings.IsEnabled;
        ReplaceProfiles(
            settings.Profiles.Select(AutopilotProfileEntry.FromSettings),
            settings.DefaultProfileId,
            SelectedProfile?.Id);
    }

    public override void Dispose()
    {
        _operationProgressService.ProgressChanged -= OnOperationProgressChanged;
        base.Dispose();
    }

    partial void OnIsAutopilotEnabledChanged(bool value)
    {
        ImportProfileCommand.NotifyCanExecuteChanged();
        DownloadProfilesCommand.NotifyCanExecuteChanged();
        RemoveSelectedProfileCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedProfileChanged(AutopilotProfileEntry? value)
    {
        RemoveSelectedProfileCommand.NotifyCanExecuteChanged();
    }

    private void MergeProfiles(IReadOnlyList<AutopilotProfileSettings> incomingProfiles)
    {
        Dictionary<string, AutopilotProfileEntry> mergedProfiles = Profiles
            .ToDictionary(profile => profile.Id, StringComparer.OrdinalIgnoreCase);

        foreach (AutopilotProfileSettings incoming in incomingProfiles)
        {
            mergedProfiles[incoming.Id] = AutopilotProfileEntry.FromSettings(incoming);
        }

        string? preferredDefaultProfileId = SelectedDefaultProfile?.Id ?? incomingProfiles.FirstOrDefault()?.Id;
        ReplaceProfiles(mergedProfiles.Values, preferredDefaultProfileId, SelectedProfile?.Id);
    }

    private void ReplaceProfiles(
        IEnumerable<AutopilotProfileEntry> profiles,
        string? preferredDefaultProfileId,
        string? preferredSelectedProfileId)
    {
        AutopilotProfileEntry[] orderedProfiles = profiles
            .OrderBy(profile => profile.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(profile => profile.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Profiles.Clear();
        foreach (AutopilotProfileEntry profile in orderedProfiles)
        {
            Profiles.Add(profile);
        }

        SelectedProfile = Profiles.FirstOrDefault(profile =>
                              string.Equals(profile.Id, preferredSelectedProfileId, StringComparison.OrdinalIgnoreCase))
                          ?? Profiles.FirstOrDefault();

        SelectedDefaultProfile = Profiles.FirstOrDefault(profile =>
                                     string.Equals(profile.Id, preferredDefaultProfileId, StringComparison.OrdinalIgnoreCase))
                                 ?? Profiles.FirstOrDefault();

        OnPropertyChanged(nameof(HasProfiles));
        RemoveSelectedProfileCommand.NotifyCanExecuteChanged();
    }

    private bool CanManageProfiles()
    {
        return IsAutopilotEnabled && !_operationProgressService.IsOperationInProgress;
    }

    private bool CanRemoveSelectedProfile()
    {
        return IsAutopilotEnabled &&
               !_operationProgressService.IsOperationInProgress &&
               SelectedProfile is not null;
    }

    private void OnOperationProgressChanged(object? sender, EventArgs e)
    {
        ImportProfileCommand.NotifyCanExecuteChanged();
        DownloadProfilesCommand.NotifyCanExecuteChanged();
        RemoveSelectedProfileCommand.NotifyCanExecuteChanged();
    }
}
