using Foundry.Core.Services.Application;
using Foundry.Services.Settings;

namespace Foundry.ViewModels
{
    public sealed partial class GeneralSettingViewModel : ObservableObject
    {
        private readonly IAppSettingsService appSettingsService;
        private readonly IExternalProcessLauncher externalProcessLauncher;

        public GeneralSettingViewModel(
            IAppSettingsService appSettingsService,
            IExternalProcessLauncher externalProcessLauncher)
        {
            this.appSettingsService = appSettingsService;
            this.externalProcessLauncher = externalProcessLauncher;
            IsDeveloperMode = appSettingsService.Current.Diagnostics.DeveloperMode;
        }

        public string LogDirectoryPath => Constants.LogDirectoryPath;

        [ObservableProperty]
        public partial bool IsDeveloperMode { get; set; }

        partial void OnIsDeveloperModeChanged(bool value)
        {
            appSettingsService.Current.Diagnostics.DeveloperMode = value;
            appSettingsService.Save();
        }

        [RelayCommand]
        private Task OpenLogFolderAsync()
        {
            return externalProcessLauncher.OpenFolderAsync(Constants.LogDirectoryPath);
        }
    }
}
