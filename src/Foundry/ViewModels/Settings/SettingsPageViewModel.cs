// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Services.Configuration;
using Foundry.Services.Settings;
using Foundry.Telemetry;

namespace Foundry.ViewModels;

public sealed partial class SettingsPageViewModel : ObservableObject
{
    private readonly IAppSettingsService appSettingsService;
    private readonly IFoundryConfigurationStateService foundryConfigurationStateService;

    public SettingsPageViewModel(
        IAppSettingsService appSettingsService,
        IFoundryConfigurationStateService foundryConfigurationStateService)
    {
        this.appSettingsService = appSettingsService;
        this.foundryConfigurationStateService = foundryConfigurationStateService;
        IsTelemetryEnabled = appSettingsService.Current.Telemetry.IsEnabled;
        IsRemoteDiagnosticsEnabled = appSettingsService.Current.Telemetry.IsRemoteDiagnosticsEnabled;
    }

    [ObservableProperty]
    public partial bool IsTelemetryEnabled { get; set; }

    [ObservableProperty]
    public partial bool IsRemoteDiagnosticsEnabled { get; set; }

    partial void OnIsTelemetryEnabledChanged(bool value)
    {
        appSettingsService.Current.Telemetry.IsEnabled = value;
        appSettingsService.Save();
        foundryConfigurationStateService.UpdateTelemetry(CreateTelemetrySettings());
    }

    partial void OnIsRemoteDiagnosticsEnabledChanged(bool value)
    {
        appSettingsService.Current.Telemetry.IsRemoteDiagnosticsEnabled = value;
        appSettingsService.Save();
        foundryConfigurationStateService.UpdateTelemetry(CreateTelemetrySettings());
    }

    private TelemetrySettings CreateTelemetrySettings()
    {
        return new TelemetrySettings
        {
            IsEnabled = appSettingsService.Current.Telemetry.IsEnabled,
            IsRemoteDiagnosticsEnabled = appSettingsService.Current.Telemetry.IsRemoteDiagnosticsEnabled,
            InstallId = appSettingsService.Current.Telemetry.InstallId,
            HostUrl = TelemetryDefaults.PostHogEuHost,
            ProjectToken = TelemetryDefaults.ProjectToken,
            RuntimePayloadSource = TelemetryRuntimePayloadSources.None
        };
    }
}
