// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using Foundry.Localization;
using Foundry.Core.Services.Application;
using Foundry.Core.Services.Media;
using Foundry.Services.Application;
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
        private readonly MediaOperationCoordinator mediaCoordinator;

        public GeneralSettingViewModel(
            IAppSettingsService appSettingsService,
            IExternalProcessLauncher externalProcessLauncher,
            IApplicationLocalizationService localizationService,
            IFilePickerService filePickerService,
            MediaOperationCoordinator mediaCoordinator)
        {
            this.appSettingsService = appSettingsService;
            this.externalProcessLauncher = externalProcessLauncher;
            this.localizationService = localizationService;
            this.filePickerService = filePickerService;
            this.mediaCoordinator = mediaCoordinator;
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
                Style = ContentDialogStyleProvider.DefaultStyle,
                Title = localizationService.GetString("Diagnostics.ExportRawTitle"),
                Content = localizationService.GetString("Diagnostics.ExportRawWarning"),
                PrimaryButtonText = localizationService.GetString("Diagnostics.ExportRawConfirm"),
                CloseButtonText = localizationService.GetString("Common.Cancel"),
                DefaultButton = ContentDialogButton.Close
            };
            Foundry.Services.Localization.LocalizationRoot.BindToMainRoot(warningDialog);
            if (await warningDialog.ShowAsync() != ContentDialogResult.Primary)
            {
                return;
            }

            await ExportDiagnosticsAsync(SupportBundlePrivacyMode.Raw);
        }

        private async Task ExportDiagnosticsAsync(SupportBundlePrivacyMode privacyMode)
        {
            string? destinationDirectoryPath = await filePickerService.PickFolderAsync(
                new FolderPickerRequest(localizationService.GetString("Diagnostics.ExportDestinationTitle")));
            if (string.IsNullOrWhiteSpace(destinationDirectoryPath))
            {
                return;
            }

            try
            {
                List<SupportBundleSource> sources = [];
                if (Path.IsPathFullyQualified(LogDirectoryPath))
                    sources.AddRange(SupportBundleSourcePolicy.CreateRollingLogs(LogDirectoryPath,
                        Path.GetFileName(LoggerSetup.LogFilePath), "authoring"));
                if (mediaCoordinator.LastDismDiagnosticLogPath is string nativePath)
                    sources.Add(SupportBundleSourcePolicy.CreateOwned(Path.GetDirectoryName(nativePath)!,
                        Path.GetFileName(nativePath), "dism-console.log", SupportBundleSourceFormat.NativeText));
                Log.ForContext<GeneralSettingViewModel>().Information(
                    "Support bundle export started. PrivacyMode={PrivacyMode}, LogFileCount={LogFileCount}",
                    privacyMode,
                    sources.Count);

                SupportBundleResult result = await new SupportBundleExporter().ExportAsync(
                    new SupportBundleRequest
                    {
                        ApplicationName = "Foundry.OSD",
                        ApplicationVersion = FoundryApplicationInfo.Version,
                        SessionId = DiagnosticSessionContext.CurrentSessionId,
                        DestinationDirectoryPath = destinationDirectoryPath,
                        Sources = sources,
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
                    Style = ContentDialogStyleProvider.DefaultStyle,
                    Title = localizationService.GetString("Diagnostics.ExportSucceededTitle"),
                    Content = result.ArchivePath,
                    CloseButtonText = localizationService.GetString("Common.Close")
                };
                Foundry.Services.Localization.LocalizationRoot.BindToMainRoot(completedDialog);
                await completedDialog.ShowAsync();
            }
            catch (Exception ex)
            {
                Log.ForContext<GeneralSettingViewModel>().Error(ex, "Support bundle export failed. PrivacyMode={PrivacyMode}", privacyMode);
                var failedDialog = new ContentDialog
                {
                    XamlRoot = App.MainWindow.Content.XamlRoot,
                    Style = ContentDialogStyleProvider.DefaultStyle,
                    Title = localizationService.GetString("Diagnostics.ExportFailedTitle"),
                    Content = localizationService.GetString("Diagnostics.ExportFailedMessage"),
                    CloseButtonText = localizationService.GetString("Common.Close")
                };
                Foundry.Services.Localization.LocalizationRoot.BindToMainRoot(failedDialog);
                await failedDialog.ShowAsync();
            }
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
