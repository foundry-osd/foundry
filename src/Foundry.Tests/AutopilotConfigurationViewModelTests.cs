using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Application;
using Foundry.Core.Services.Configuration;
using Foundry.Localization;
using Foundry.Services.Autopilot;
using Foundry.Services.Configuration;
using Foundry.Services.Localization;
using Foundry.Telemetry;
using Foundry.ViewModels;
using Serilog;

namespace Foundry.Tests;

public sealed class AutopilotConfigurationViewModelTests
{
    [Fact]
    public void UseHardwareHashUploadProvisioning_WhenSelected_PersistsModeAndExistingHashSettings()
    {
        AutopilotHardwareHashUploadSettings hashSettings = CreateHardwareHashUploadSettings();
        var configurationState = new FakeFoundryConfigurationStateService(new AutopilotSettings
        {
            IsEnabled = true,
            ProvisioningMode = AutopilotProvisioningMode.JsonProfile,
            HardwareHashUpload = hashSettings
        });
        using AutopilotConfigurationViewModel viewModel = CreateViewModel(configurationState);

        viewModel.UseHardwareHashUploadProvisioning = true;

        Assert.Equal(AutopilotProvisioningMode.HardwareHashUpload, configurationState.LastAutopilotSettings.ProvisioningMode);
        Assert.Same(hashSettings, configurationState.LastAutopilotSettings.HardwareHashUpload);
        Assert.False(viewModel.UseJsonProfileProvisioning);
    }

    [Fact]
    public void UseJsonProfileProvisioning_WhenSelected_PersistsModeAndExistingHashSettings()
    {
        AutopilotHardwareHashUploadSettings hashSettings = CreateHardwareHashUploadSettings();
        var configurationState = new FakeFoundryConfigurationStateService(new AutopilotSettings
        {
            IsEnabled = true,
            ProvisioningMode = AutopilotProvisioningMode.HardwareHashUpload,
            HardwareHashUpload = hashSettings
        });
        using AutopilotConfigurationViewModel viewModel = CreateViewModel(configurationState);

        viewModel.UseJsonProfileProvisioning = true;

        Assert.Equal(AutopilotProvisioningMode.JsonProfile, configurationState.LastAutopilotSettings.ProvisioningMode);
        Assert.Same(hashSettings, configurationState.LastAutopilotSettings.HardwareHashUpload);
        Assert.False(viewModel.UseHardwareHashUploadProvisioning);
    }

    private static AutopilotConfigurationViewModel CreateViewModel(FakeFoundryConfigurationStateService configurationState)
    {
        return new AutopilotConfigurationViewModel(
            configurationState,
            new FakeAutopilotProfileImportService(),
            new FakeAutopilotTenantProfileService(),
            new FakeAutopilotTenantDownloadDialogService(),
            new FakeAutopilotProfileSelectionDialogService(),
            new FakeFilePickerService(),
            new FakeDialogService(),
            new FakeApplicationLocalizationService(),
            new LoggerConfiguration().CreateLogger());
    }

    private static AutopilotHardwareHashUploadSettings CreateHardwareHashUploadSettings()
    {
        return new AutopilotHardwareHashUploadSettings
        {
            Tenant = new AutopilotTenantRegistrationSettings
            {
                TenantId = "tenant-id",
                ApplicationObjectId = "application-object-id",
                ClientId = "client-id",
                ServicePrincipalObjectId = "service-principal-object-id"
            },
            ActiveCertificate = new AutopilotCertificateMetadata
            {
                KeyId = "certificate-key-id",
                Thumbprint = "ABCDEF123456",
                DisplayName = "Foundry OSD Autopilot Registration",
                ExpiresOnUtc = DateTimeOffset.UtcNow.AddMonths(12)
            },
            KnownGroupTags = ["Sales", "Engineering"],
            DefaultGroupTag = "Sales"
        };
    }

    private sealed class FakeFoundryConfigurationStateService : IFoundryConfigurationStateService
    {
        public FakeFoundryConfigurationStateService(AutopilotSettings autopilotSettings)
        {
            Current = new FoundryConfigurationDocument
            {
                Autopilot = autopilotSettings
            };
            LastAutopilotSettings = autopilotSettings;
        }

        public event EventHandler? StateChanged;

        public FoundryConfigurationDocument Current { get; private set; }
        public AutopilotSettings LastAutopilotSettings { get; private set; }
        public bool IsNetworkConfigurationReady => true;
        public bool IsDeployConfigurationReady => true;
        public bool IsConnectProvisioningReady => true;
        public bool AreRequiredSecretsReady => true;
        public bool IsAutopilotEnabled => Current.Autopilot.IsEnabled;
        public bool IsAutopilotConfigurationReady => true;
        public AutopilotProvisioningMode AutopilotProvisioningMode => Current.Autopilot.ProvisioningMode;
        public string? SelectedAutopilotProfileDisplayName => null;
        public string? SelectedAutopilotProfileFolderName => null;

        public void UpdateGeneral(GeneralSettings settings)
        {
        }

        public void UpdateNetwork(NetworkSettings settings)
        {
        }

        public void UpdateLocalization(LocalizationSettings settings)
        {
        }

        public void UpdateCustomization(CustomizationSettings settings)
        {
        }

        public void UpdateAutopilot(AutopilotSettings settings)
        {
            LastAutopilotSettings = settings;
            Current = Current with { Autopilot = settings };
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        public void UpdateTelemetry(TelemetrySettings settings)
        {
        }

        public FoundryConnectProvisioningBundle GenerateConnectProvisioningBundle(
            string stagingDirectoryPath,
            TelemetrySettings? telemetryOverride = null)
        {
            throw new NotSupportedException();
        }

        public string GenerateDeployConfigurationJson(TelemetrySettings? telemetryOverride = null)
        {
            return "{}";
        }
    }

    private sealed class FakeAutopilotProfileImportService : IAutopilotProfileImportService
    {
        public Task<AutopilotProfileSettings> ImportFromJsonFileAsync(
            string filePath,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class FakeAutopilotTenantProfileService : IAutopilotTenantProfileService
    {
        public Task<IReadOnlyList<AutopilotProfileSettings>> DownloadFromTenantAsync(
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class FakeAutopilotTenantDownloadDialogService : IAutopilotTenantDownloadDialogService
    {
        public Task<IReadOnlyList<AutopilotProfileSettings>?> DownloadAsync(
            Func<CancellationToken, Task<IReadOnlyList<AutopilotProfileSettings>>> downloadProfilesAsync)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class FakeAutopilotProfileSelectionDialogService : IAutopilotProfileSelectionDialogService
    {
        public Task<IReadOnlyList<AutopilotProfileSettings>?> PickProfilesAsync(
            IReadOnlyList<AutopilotProfileSettings> availableProfiles)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class FakeFilePickerService : IFilePickerService
    {
        public Task<string?> PickOpenFileAsync(FileOpenPickerRequest request)
        {
            return Task.FromResult<string?>(null);
        }

        public Task<string?> PickSaveFileAsync(FileSavePickerRequest request)
        {
            return Task.FromResult<string?>(null);
        }

        public Task<string?> PickFolderAsync(FolderPickerRequest request)
        {
            return Task.FromResult<string?>(null);
        }
    }

    private sealed class FakeDialogService : IDialogService
    {
        public Task ShowMessageAsync(DialogRequest request)
        {
            return Task.CompletedTask;
        }

        public Task<bool> ConfirmAsync(ConfirmationDialogRequest request)
        {
            return Task.FromResult(false);
        }
    }

    private sealed class FakeApplicationLocalizationService : IApplicationLocalizationService
    {
        public string CurrentLanguage => "en-US";

        public event EventHandler<ApplicationLanguageChangedEventArgs>? LanguageChanged;

        public Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task SetLanguageAsync(string languageCode, CancellationToken cancellationToken = default)
        {
            LanguageChanged?.Invoke(this, new ApplicationLanguageChangedEventArgs(CurrentLanguage, languageCode));
            return Task.CompletedTask;
        }

        public string GetString(string key)
        {
            return key;
        }

        public string FormatString(string key, params object[] args)
        {
            return string.Format(key, args);
        }

        public IReadOnlyList<SupportedCultureOption> CreateSupportedLanguageOptions()
        {
            return [];
        }
    }
}
