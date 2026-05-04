using System.Collections.ObjectModel;
using Foundry.Core.Localization;
using Foundry.Core.Services.Application;
using Foundry.Core.Services.WinPe;
using Foundry.Services.Adk;
using Foundry.Services.Localization;
using Foundry.Services.Settings;
using Serilog;

namespace Foundry.ViewModels
{
    public sealed partial class GeneralSettingViewModel : ObservableObject
    {
        private readonly IAppSettingsService appSettingsService;
        private readonly IExternalProcessLauncher externalProcessLauncher;
        private readonly IApplicationLocalizationService localizationService;
        private readonly IAdkService adkService;
        private readonly IWinPeLanguageDiscoveryService winPeLanguageDiscoveryService;
        private readonly ILogger logger;

        public GeneralSettingViewModel(
            IAppSettingsService appSettingsService,
            IExternalProcessLauncher externalProcessLauncher,
            IApplicationLocalizationService localizationService,
            IAdkService adkService,
            IWinPeLanguageDiscoveryService winPeLanguageDiscoveryService,
            ILogger logger)
        {
            this.appSettingsService = appSettingsService;
            this.externalProcessLauncher = externalProcessLauncher;
            this.localizationService = localizationService;
            this.adkService = adkService;
            this.winPeLanguageDiscoveryService = winPeLanguageDiscoveryService;
            this.logger = logger.ForContext<GeneralSettingViewModel>();
            IsDeveloperMode = appSettingsService.Current.Diagnostics.DeveloperMode;
            RefreshSupportedLanguages();
        }

        public ObservableCollection<SupportedCultureOption> SupportedLanguages { get; } = [];
        public ObservableCollection<string> AvailableWinPeLanguages { get; } = [];

        public string LogDirectoryPath => Constants.LogDirectoryPath;

        [ObservableProperty]
        public partial bool IsDeveloperMode { get; set; }

        [ObservableProperty]
        public partial SupportedCultureOption? SelectedLanguage { get; set; }

        [ObservableProperty]
        public partial string? SelectedWinPeLanguage { get; set; }

        [ObservableProperty]
        public partial bool HasWinPeLanguages { get; set; }

        [ObservableProperty]
        public partial string WinPeLanguageStatus { get; set; } = string.Empty;

        public async Task SetLanguageAsync(SupportedCultureOption? selectedLanguage)
        {
            if (selectedLanguage is null)
            {
                return;
            }

            await localizationService.SetLanguageAsync(selectedLanguage.Code);
        }

        public void SetWinPeLanguage(string? selectedLanguage)
        {
            if (string.IsNullOrWhiteSpace(selectedLanguage))
            {
                return;
            }

            appSettingsService.Current.Media.WinPeLanguage = selectedLanguage;
            appSettingsService.Save();
            SelectedWinPeLanguage = selectedLanguage;
        }

        partial void OnIsDeveloperModeChanged(bool value)
        {
            appSettingsService.Current.Diagnostics.DeveloperMode = value;
            appSettingsService.Save();
            SetDeveloperModeEnabled(value);
        }

        [RelayCommand]
        private Task OpenLogFolderAsync()
        {
            return externalProcessLauncher.OpenFolderAsync(Constants.LogDirectoryPath);
        }

        [RelayCommand]
        public Task RefreshWinPeLanguagesAsync()
        {
            RefreshWinPeLanguages();
            return Task.CompletedTask;
        }

        public void RefreshSupportedLanguages()
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

        public void RefreshWinPeLanguages()
        {
            AvailableWinPeLanguages.Clear();
            HasWinPeLanguages = false;

            if (!adkService.CurrentStatus.CanCreateMedia)
            {
                WinPeLanguageStatus = localizationService.GetString("GeneralSetting_WinPeLanguage.NotReady");
                SelectedWinPeLanguage = null;
                return;
            }

            WinPeResult<WinPeToolPaths> toolsResult = new WinPeToolResolver().ResolveTools(adkService.CurrentStatus.KitsRootPath);
            if (!toolsResult.IsSuccess || toolsResult.Value is null)
            {
                WinPeLanguageStatus = toolsResult.Error?.Message ?? localizationService.GetString("GeneralSetting_WinPeLanguage.NotReady");
                logger.Warning("WinPE language discovery skipped because ADK tools were not resolved. ErrorCode={ErrorCode}", toolsResult.Error?.Code);
                SelectedWinPeLanguage = null;
                return;
            }

            WinPeArchitecture architecture = ParseArchitecture(appSettingsService.Current.Media.Architecture);
            WinPeResult<IReadOnlyList<string>> result = winPeLanguageDiscoveryService.GetAvailableLanguages(
                new WinPeLanguageDiscoveryOptions
                {
                    Architecture = architecture,
                    Tools = toolsResult.Value
                });

            if (!result.IsSuccess || result.Value is null || result.Value.Count == 0)
            {
                WinPeLanguageStatus = result.Error?.Message ?? localizationService.GetString("GeneralSetting_WinPeLanguage.Empty");
                logger.Warning(
                    "WinPE language discovery returned no languages. Architecture={Architecture}, ErrorCode={ErrorCode}",
                    architecture,
                    result.Error?.Code);
                SelectedWinPeLanguage = null;
                return;
            }

            string? previousSelection = appSettingsService.Current.Media.WinPeLanguage;
            string selected = SelectWinPeLanguage(result.Value, previousSelection, localizationService.CurrentLanguage);

            foreach (string language in result.Value)
            {
                AvailableWinPeLanguages.Add(language);
            }

            HasWinPeLanguages = true;
            WinPeLanguageStatus = string.Format(
                localizationService.GetString("GeneralSetting_WinPeLanguage.Status"),
                result.Value.Count);
            SelectedWinPeLanguage = selected;
            appSettingsService.Current.Media.WinPeLanguage = selected;
            appSettingsService.Save();
        }

        private static WinPeArchitecture ParseArchitecture(string? value)
        {
            return Enum.TryParse(value, ignoreCase: true, out WinPeArchitecture architecture)
                ? architecture
                : WinPeArchitecture.X64;
        }

        private static string SelectWinPeLanguage(IReadOnlyList<string> languages, string? preferredLanguage, string currentLanguage)
        {
            string? exact = languages.FirstOrDefault(language => string.Equals(language, preferredLanguage, StringComparison.OrdinalIgnoreCase))
                ?? languages.FirstOrDefault(language => string.Equals(language, currentLanguage, StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrWhiteSpace(exact))
            {
                return exact;
            }

            string currentPrefix = currentLanguage.Split('-', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
            string? family = languages.FirstOrDefault(language => language.StartsWith(currentPrefix + "-", StringComparison.OrdinalIgnoreCase));

            return family ?? languages[0];
        }
    }
}
