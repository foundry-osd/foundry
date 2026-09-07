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
    private readonly IRemoteDiagnosticsService remoteDiagnosticsService;
    private readonly TelemetryContext telemetryContext;
    private readonly ITelemetryService telemetryService;
    private bool restoringPreference = true;

    public SettingsPageViewModel(
        IAppSettingsService appSettingsService,
        IFoundryConfigurationStateService foundryConfigurationStateService,
        IRemoteDiagnosticsService remoteDiagnosticsService,
        TelemetryContext telemetryContext,
        ITelemetryService telemetryService)
    {
        this.appSettingsService = appSettingsService;
        this.foundryConfigurationStateService = foundryConfigurationStateService;
        this.remoteDiagnosticsService = remoteDiagnosticsService;
        this.telemetryContext = telemetryContext;
        this.telemetryService = telemetryService;
        IsTelemetryEnabled = appSettingsService.Current.Telemetry.IsEnabled;
        IsRemoteDiagnosticsEnabled = appSettingsService.Current.Telemetry.IsRemoteDiagnosticsEnabled;
        restoringPreference = false;
    }

    [ObservableProperty]
    public partial bool IsTelemetryEnabled { get; set; }

    [ObservableProperty]
    public partial bool IsRemoteDiagnosticsEnabled { get; set; }

    partial void OnIsTelemetryEnabledChanged(bool value)
    {
        if (restoringPreference) return;
        bool previous = appSettingsService.Current.Telemetry.IsEnabled;
        appSettingsService.Current.Telemetry.IsEnabled = value;
        try { appSettingsService.Save(); }
        catch
        {
            appSettingsService.Current.Telemetry.IsEnabled = previous;
            restoringPreference = true;
            try { IsTelemetryEnabled = previous; }
            finally { restoringPreference = false; }
            return;
        }
        telemetryService.SetEnabled(value);
        foundryConfigurationStateService.UpdateTelemetry(CreateTelemetrySettings());
    }

    partial void OnIsRemoteDiagnosticsEnabledChanged(bool value)
    {
        if (restoringPreference) return;
        bool previous = appSettingsService.Current.Telemetry.IsRemoteDiagnosticsEnabled;
        appSettingsService.Current.Telemetry.IsRemoteDiagnosticsEnabled = value;
        try { appSettingsService.Save(); }
        catch
        {
            appSettingsService.Current.Telemetry.IsRemoteDiagnosticsEnabled = previous;
            restoringPreference = true;
            try { IsRemoteDiagnosticsEnabled = previous; }
            finally { restoringPreference = false; }
            return;
        }
        TelemetrySettings settings = CreateTelemetrySettings();
        RemoteDiagnosticsLifecycle.Initialize(remoteDiagnosticsService, settings, telemetryContext);
        foundryConfigurationStateService.UpdateTelemetry(settings);
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
