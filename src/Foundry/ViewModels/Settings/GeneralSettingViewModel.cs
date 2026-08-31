// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using Foundry.Localization;
using Foundry.Core.Services.Application;
using Foundry.Services.Localization;
using Foundry.Services.Settings;
using Foundry.Utilities.Diagnostics;
using Microsoft.UI.Xaml.Controls;
using Serilog;

namespace Foundry.ViewModels
{
    public sealed partial class GeneralSettingViewModel : ObservableObject
    {
        private readonly IAppSettingsService appSettingsService;
        private readonly IExternalProcessLauncher externalProcessLauncher;
        private readonly IApplicationLocalizationService localizationService;
        private readonly IFilePickerService filePickerService;

        public GeneralSettingViewModel(
            IAppSettingsService appSettingsService,
            IExternalProcessLauncher externalProcessLauncher,
            IApplicationLocalizationService localizationService,
            IFilePickerService filePickerService)
        {
            this.appSettingsService = appSettingsService;
            this.externalProcessLauncher = externalProcessLauncher;
            this.localizationService = localizationService;
            this.filePickerService = filePickerService;
            IsDeveloperMode = appSettingsService.Current.Diagnostics.DeveloperMode;
            RefreshSupportedLanguages();
        }

        public ObservableCollection<SupportedCultureOption> SupportedLanguages { get; } = [];

        public string LogDirectoryPath => LoggerSetup.LogFilePath == "<unavailable>"
            ? LoggerSetup.LogFilePath
            : Path.GetDirectoryName(LoggerSetup.LogFilePath) ?? Constants.LogDirectoryPath;

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
            return Directory.Exists(LogDirectoryPath)
                ? externalProcessLauncher.OpenFolderAsync(LogDirectoryPath)
                : Task.CompletedTask;
        }

        [RelayCommand]
        private Task ExportDiagnosticsAsync()
        {
            return ExportDiagnosticsAsync(SupportBundlePrivacyMode.Sanitized);
        }

        [RelayCommand]
        private async Task ExportRawDiagnosticsAsync()
        {
            var warningDialog = new ContentDialog
            {
                XamlRoot = App.MainWindow.Content.XamlRoot,
                Title = "Export raw diagnostics?",
                Content = "Raw logs may contain credentials, identifiers, paths, network names, and other sensitive data. Export them only when explicitly requested by a trusted support contact.",
                PrimaryButtonText = "Export raw logs",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close
            };
            if (await warningDialog.ShowAsync() != ContentDialogResult.Primary)
            {
                return;
            }

            await ExportDiagnosticsAsync(SupportBundlePrivacyMode.Raw);
        }

        private async Task ExportDiagnosticsAsync(SupportBundlePrivacyMode privacyMode)
        {
            string? destinationDirectoryPath = await filePickerService.PickFolderAsync(
                new FolderPickerRequest("Choose where to export Foundry diagnostics"));
            if (string.IsNullOrWhiteSpace(destinationDirectoryPath))
            {
                return;
            }

            string[] logFilePaths = Directory.Exists(LogDirectoryPath)
                ? Directory.GetFiles(LogDirectoryPath, "Foundry*.log", SearchOption.TopDirectoryOnly)
                : [];
            Log.ForContext<GeneralSettingViewModel>().Information(
                "Support bundle export started. PrivacyMode={PrivacyMode}, LogFileCount={LogFileCount}",
                privacyMode,
                logFilePaths.Length);
            await Task.Delay(TimeSpan.FromSeconds(1));

            SupportBundleResult result = await new SupportBundleExporter().ExportAsync(
                new SupportBundleRequest
                {
                    ApplicationName = "Foundry.OSD",
                    ApplicationVersion = FoundryApplicationInfo.Version,
                    SessionId = DiagnosticSessionContext.CurrentSessionId,
                    DestinationDirectoryPath = destinationDirectoryPath,
                    LogFilePaths = logFilePaths,
                    PrivacyMode = privacyMode,
                    Summary = new Dictionary<string, string>
                    {
                        ["Architecture"] = RuntimeInformation.ProcessArchitecture.ToString(),
                        ["OperatingSystem"] = Environment.OSVersion.VersionString
                    }
                });

            Log.ForContext<GeneralSettingViewModel>().Information(
                "Support bundle export completed. PrivacyMode={PrivacyMode}, IncludedFileCount={IncludedFileCount}, OmittedFileCount={OmittedFileCount}",
                privacyMode,
                result.IncludedFiles.Count,
                result.OmittedFiles.Count);
            var completedDialog = new ContentDialog
            {
                XamlRoot = App.MainWindow.Content.XamlRoot,
                Title = "Diagnostics exported",
                Content = result.ArchivePath,
                CloseButtonText = "Close"
            };
            await completedDialog.ShowAsync();
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

    }
}
