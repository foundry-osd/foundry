using Foundry.Services.Configuration;
using Foundry.Services.Settings;
using Foundry.Telemetry;

namespace Foundry.ViewModels;

public sealed partial class SettingsPageViewModel : ObservableObject
{
    private readonly IAppSettingsService appSettingsService;
    private readonly IExpertDeployConfigurationStateService expertDeployConfigurationStateService;

    public SettingsPageViewModel(
        IAppSettingsService appSettingsService,
        IExpertDeployConfigurationStateService expertDeployConfigurationStateService)
    {
        this.appSettingsService = appSettingsService;
        this.expertDeployConfigurationStateService = expertDeployConfigurationStateService;
        IsTelemetryEnabled = appSettingsService.Current.Telemetry.IsEnabled;
    }

    [ObservableProperty]
    public partial bool IsTelemetryEnabled { get; set; }

    partial void OnIsTelemetryEnabledChanged(bool value)
    {
        appSettingsService.Current.Telemetry.IsEnabled = value;
        appSettingsService.Save();
        expertDeployConfigurationStateService.UpdateTelemetry(CreateTelemetrySettings());
    }

    private TelemetrySettings CreateTelemetrySettings()
    {
        return new TelemetrySettings
        {
            IsEnabled = appSettingsService.Current.Telemetry.IsEnabled,
            InstallId = appSettingsService.Current.Telemetry.InstallId,
            HostUrl = TelemetryDefaults.PostHogEuHost,
            ProjectToken = TelemetryDefaults.ProjectToken,
            RuntimePayloadSource = TelemetryRuntimePayloadSources.None
        };
    }
}
