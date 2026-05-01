using System.Collections.ObjectModel;
using Foundry.Core.Localization;
using Foundry.Core.Services.Application;
using Foundry.Services.Localization;
using Foundry.Services.Settings;

namespace Foundry.ViewModels
{
    public sealed partial class GeneralSettingViewModel : ObservableObject
    {
        private readonly IAppSettingsService appSettingsService;
        private readonly IExternalProcessLauncher externalProcessLauncher;
        private readonly IApplicationLocalizationService localizationService;

        public GeneralSettingViewModel(
            IAppSettingsService appSettingsService,
            IExternalProcessLauncher externalProcessLauncher,
            IApplicationLocalizationService localizationService)
        {
            this.appSettingsService = appSettingsService;
            this.externalProcessLauncher = externalProcessLauncher;
            this.localizationService = localizationService;
            IsDeveloperMode = appSettingsService.Current.Diagnostics.DeveloperMode;
            RefreshSupportedLanguages();
        }

        public ObservableCollection<SupportedCultureOption> SupportedLanguages { get; } = [];

        public string LogDirectoryPath => Constants.LogDirectoryPath;

        [ObservableProperty]
        public partial bool IsDeveloperMode { get; set; }

        [ObservableProperty]
        public partial SupportedCultureOption? SelectedLanguage { get; set; }

        public async Task SetLanguageAsync(SupportedCultureOption? selectedLanguage)
        {
            if (selectedLanguage is null)
            {
                return;
            }

            await localizationService.SetLanguageAsync(selectedLanguage.Code);
            RefreshSupportedLanguages();
        }

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

        private void RefreshSupportedLanguages()
        {
            SupportedLanguages.Clear();

            SupportedCultureOption? selectedOption = null;
            foreach (SupportedCultureOption option in localizationService.CreateSupportedLanguageOptions())
            {
                SupportedLanguages.Add(option);
                if (option.IsSelected)
                {
                    selectedOption = option;
                }
            }

            SelectedLanguage = selectedOption;
        }
    }
}
