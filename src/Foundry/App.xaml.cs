// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.DependencyInjection;
using Foundry.Core.Services.Profiles;
using Foundry.Core.Services.Application;
using Foundry.Services.Configuration;
using Foundry.Services.Application;
using Foundry.Services.Appearance;
using Foundry.Services.Localization;
using Foundry.Services.Networking;
using Foundry.Services.Settings;
using Foundry.Services.Shell;
using Foundry.Services.Startup;
using Foundry.Services.Updates;
using Foundry.Telemetry;
using Microsoft.UI.Xaml;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace Foundry
{
    /// <summary>
    /// Owns the WinUI application lifetime and exposes the host services used by XAML pages.
    /// </summary>
    public partial class App : Application
    {
        private static readonly ILogger AppLogger = Log.ForContext<App>();
        private static readonly TimeSpan RemoteDiagnosticsShutdownTimeout = TimeSpan.FromSeconds(2);
        private bool isShuttingDown;
        private WindowsSessionEndingMonitor? sessionEndingMonitor;
        private long sessionChangeVersion;

        /// <summary>
        /// Gets the active Foundry application instance.
        /// </summary>
        public new static App Current => (App)Application.Current;

        /// <summary>
        /// Gets the main window once the application has launched.
        /// </summary>
        public static Window MainWindow = Window.Current;

        /// <summary>
        /// Gets the dependency injection host for the application.
        /// </summary>
        public IHost Host { get; }

        /// <summary>
        /// Gets the service provider rooted in <see cref="Host"/>.
        /// </summary>
        public IServiceProvider Services => Host.Services;

        /// <summary>
        /// Gets the native WinUI shell navigation service.
        /// </summary>
        public IAppNavigationService NavigationService => GetService<IAppNavigationService>();

        /// <summary>
        /// Gets the theme service used to apply runtime theme changes.
        /// </summary>
        public IAppThemeService ThemeService => GetService<IAppThemeService>();

        /// <summary>
        /// Resolves a required service from the application host.
        /// </summary>
        /// <typeparam name="T">The service contract type.</typeparam>
        /// <returns>The registered service instance.</returns>
        /// <exception cref="ArgumentException">Thrown when the requested service has not been registered.</exception>
        public static T GetService<T>() where T : class
        {
            if (Current.Services.GetService(typeof(T)) is not T service)
            {
                throw new ArgumentException($"{typeof(T)} needs to be registered in {nameof(ServiceCollectionExtensions)}.");
            }

            return service;
        }

        /// <summary>
        /// Creates the application host, initializes settings and localization, and loads the XAML application.
        /// </summary>
        public App()
        {
            Host = FoundryHost.Create();
            _ = Host.Services.GetRequiredService<IApplicationProxyService>();
            InitializeRemoteDiagnostics(Host.Services.GetRequiredService<TelemetrySettings>());
            _ = Host.Services.GetRequiredService<IFoundryConfigurationStateService>();
            Host.Services.GetRequiredService<IApplicationLocalizationService>().InitializeAsync().GetAwaiter().GetResult();
            RegisterWinUiExceptionHandler();

            AppLogger.Information("Foundry WinUI host initialized.");
            this.InitializeComponent();
        }

        /// <summary>
        /// Creates and activates the main window, then starts application readiness checks.
        /// </summary>
        /// <param name="args">Launch activation arguments provided by WinUI.</param>
        protected override async void OnLaunched(LaunchActivatedEventArgs args)
        {
            try
            {
                MainWindow mainWindow = GetService<MainWindow>();
                MainWindow = mainWindow;
                mainWindow.Closed += OnMainWindowClosed;
                mainWindow.AppWindow.Closing += OnMainWindowClosing;
                sessionEndingMonitor = new WindowsSessionEndingMonitor(WinRT.Interop.WindowNative.GetWindowHandle(mainWindow));
                sessionEndingMonitor.StateChanged += OnSessionEndingStateChanged;

                mainWindow.Title = mainWindow.AppWindow.Title = FoundryApplicationInfo.AppNameAndVersion;
                mainWindow.AppWindow.SetIcon("Assets/AppIcon.ico");

                ThemeService.Initialize(mainWindow, mainWindow.RootElement);

                mainWindow.Activate();

                await InitializeAppAsync();
            }
            catch (Exception ex)
            {
                AppLogger.Error(ex, "Foundry WinUI launch failed.");
                throw;
            }
        }

        private static async Task InitializeAppAsync()
        {
            await Task.Run(() => DeploymentBuildSnapshot.CleanupAbandoned(Path.Combine(Constants.UserRootDirectoryPath, "BuildSnapshots")));
            await GetService<DeploymentProfileCoordinator>().InitializeAsync();
            await GetService<IStartupReadinessService>().InitializeAsync();
            GetService<DeploymentProfileCoordinator>().StartAutomaticSynchronization();
            await TrackDailyActiveAsync();
            AppLogger.Information("Foundry WinUI startup completed.");
        }

        private static async Task TrackDailyActiveAsync()
        {
            IAppSettingsService settingsService = GetService<IAppSettingsService>();
            if (!settingsService.Current.Telemetry.IsEnabled)
            {
                return;
            }

            DateOnly today = DateOnly.FromDateTime(DateTime.Now);
            if (!TelemetryDailyActivityGate.ShouldTrack(today, settingsService.Current.Telemetry.LastDailyActiveDate))
            {
                return;
            }

            AppLogger.Debug("Tracking Foundry daily-active telemetry event.");
            ProxyAppSettings proxy = settingsService.Current.Proxy;
            await GetService<ITelemetryService>().TrackAsync(TelemetryEvents.AppDailyActive, new Dictionary<string, object?>
            {
                ["proxy_method"] = proxy.Method.ToString().ToLowerInvariant(),
                ["proxy_authentication_mode"] = proxy.Method == ProxyMethod.Manual
                    ? proxy.AuthenticationMode.ToString().ToLowerInvariant()
                    : "not_applicable"
            });
            settingsService.Current.Telemetry.LastDailyActiveDate = TelemetryDailyActivityGate.FormatDate(today);
            settingsService.Save();
            AppLogger.Debug("Foundry daily-active telemetry event queued.");
        }

        private void RegisterWinUiExceptionHandler()
        {
            UnhandledException += OnWinUiUnhandledException;
        }

        private static void OnWinUiUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
        {
            AppLogger.Fatal(e.Exception, "Unhandled WinUI exception.");
        }

        private bool closeApproved;
        private bool closePending;

        private async void OnMainWindowClosing(Microsoft.UI.Windowing.AppWindow sender, Microsoft.UI.Windowing.AppWindowClosingEventArgs args)
        {
            if (closeApproved || sessionEndingMonitor?.IsSessionEnding == true) return;
            args.Cancel = true;
            await RequestCloseAsync(restartForUpdate: false);
        }

        /// <summary>
        /// Saves and protects the active profile before ordinary exit or an explicit prepared-update restart.
        /// </summary>
        /// <param name="restartForUpdate">Whether closing requires a prepared update and a successful visible restart handoff.</param>
        /// <returns>Whether application closing was committed.</returns>
        internal async Task<bool> RequestCloseAsync(bool restartForUpdate)
        {
            if (!MainWindow.DispatcherQueue.HasThreadAccess)
            {
                var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                if (!MainWindow.DispatcherQueue.TryEnqueue(async () =>
                    {
                        try { completion.SetResult(await RequestCloseAsync(restartForUpdate)); }
                        catch (Exception ex) { completion.SetException(ex); }
                    }))
                {
                    return false;
                }
                return await completion.Task;
            }

            if (closeApproved || closePending || isShuttingDown || sessionEndingMonitor?.IsSessionEnding == true) return false;
            IShellNavigationGuardService navigationGuard = GetService<IShellNavigationGuardService>();
            if (navigationGuard.State is ShellNavigationState.OperationRunning or ShellNavigationState.InteractionPending) return false;
            if (restartForUpdate && !IsPreparedUpdateReady()) return false;

            closePending = true;
            long closeSessionVersion = sessionChangeVersion;
            IApplicationUpdateService updates = GetService<IApplicationUpdateService>();
            IDisposable? activationSuspension = null;
            bool shutdownStarted = false;
            try
            {
                DeploymentProfileCoordinator coordinator = GetService<DeploymentProfileCoordinator>();
                activationSuspension = coordinator.SuspendActivation();
                bool saved;
                try { saved = await coordinator.FlushBeforeCloseAsync().WaitAsync(TimeSpan.FromSeconds(10)); }
                catch (TimeoutException) { saved = false; }
                if (!CanFinishClose(coordinator, closeSessionVersion)) return false;
                if (!saved)
                {
                    var localization = GetService<IApplicationLocalizationService>();
                    var dialog = new ContentDialog
                    {
                        Style = ContentDialogStyleProvider.DefaultStyle,
                        XamlRoot = ((FrameworkElement)MainWindow.Content).XamlRoot,
                        Title = localization.GetString("Profiles.Heading"),
                        Content = localization.GetString(coordinator.StatusKey == "Profiles.Incomplete" ? "Profiles.Incomplete" : "Profiles.Failed"),
                        PrimaryButtonText = localization.GetString("Common.Close"),
                        CloseButtonText = localization.GetString("Common.Cancel"),
                        DefaultButton = ContentDialogButton.Close
                    };
                    if (await dialog.ShowAsync() != ContentDialogResult.Primary) return false;
                }

                if (!CanFinishClose(coordinator, closeSessionVersion)) return false;
                if (restartForUpdate && !IsPreparedUpdateReady()) return false;

                updates.BeginShutdown();
                shutdownStarted = true;
                if (sessionEndingMonitor?.IsSessionEnding == true) return false;
                bool scheduled = false;
                try
                {
                    scheduled = updates.TrySchedulePreparedUpdate(restartForUpdate);
                }
                catch (Exception ex)
                {
                    if (restartForUpdate)
                    {
                        if (sessionEndingMonitor?.IsSessionEnding != true)
                        {
                            updates.CancelShutdown();
                            shutdownStarted = false;
                            await ShowUpdateHandoffFailureAsync(ex);
                        }
                        return false;
                    }

                    AppLogger.Warning(ex, "Prepared update could not be scheduled during ordinary close. Closing without applying it.");
                }

                if (restartForUpdate && !scheduled) return false;
                if (scheduled && restartForUpdate) coordinator.DeferStagingCleanupUntilNextLaunch();
                closeApproved = true;
                MainWindow.Close();
                return true;
            }
            catch (Exception ex)
            {
                AppLogger.Warning(ex, "Unable to finish the protected application close. RestartForUpdate={RestartForUpdate}", restartForUpdate);
                return false;
            }
            finally
            {
                if (shutdownStarted && !closeApproved && sessionEndingMonitor?.IsSessionEnding != true)
                {
                    updates.CancelShutdown();
                }
                activationSuspension?.Dispose();
                closePending = false;
            }
        }

        private bool CanFinishClose(DeploymentProfileCoordinator coordinator, long closeSessionVersion)
        {
            return !isShuttingDown && sessionEndingMonitor?.IsSessionEnding != true && sessionChangeVersion == closeSessionVersion
                && GetService<IShellNavigationGuardService>().State == ShellNavigationState.InteractionPending
                && coordinator.ActivationNavigationState is not (ShellNavigationState.OperationRunning or ShellNavigationState.InteractionPending);
        }

        private static bool IsPreparedUpdateReady() => GetService<IApplicationUpdateStateService>().CurrentResult?.IsReadyToApply == true;

        private static Task ShowUpdateHandoffFailureAsync(Exception exception)
        {
            IApplicationLocalizationService localization = GetService<IApplicationLocalizationService>();
            return GetService<IDialogService>().ShowMessageAsync(new DialogRequest(
                localization.GetString("Update.StatusTitle.ApplyFailed"),
                localization.FormatString("Update.Status.ApplyFailedFormat", exception.Message),
                localization.GetString("Common.Close")));
        }

        private void OnSessionEndingStateChanged(object? sender, EventArgs args)
        {
            sessionChangeVersion++;
            bool sessionEnding = sessionEndingMonitor?.IsSessionEnding == true;
            MainWindow.DispatcherQueue.TryEnqueue(() =>
            {
                if (isShuttingDown || closeApproved) return;
                IApplicationUpdateService updates = GetService<IApplicationUpdateService>();
                if (sessionEnding) updates.BeginShutdown();
                else updates.CancelShutdown();
            });
        }

        private void OnMainWindowClosed(object sender, WindowEventArgs args)
        {
            if (isShuttingDown)
            {
                return;
            }

            isShuttingDown = true;
            GetService<IApplicationUpdateService>().BeginShutdown();
            if (sessionEndingMonitor is not null)
            {
                sessionEndingMonitor.StateChanged -= OnSessionEndingStateChanged;
                sessionEndingMonitor.Dispose();
            }
            AppLogger.Information("Foundry WinUI shutdown started.");
            AppLogger.Debug("Flushing Foundry telemetry events.");
            GetService<ITelemetryService>().FlushAsync().GetAwaiter().GetResult();
            AppLogger.Debug("Foundry telemetry flush completed.");
            AppLogger.Information("Foundry WinUI application work completed. Closing diagnostics and services.");
            ShutdownRemoteDiagnostics();
            Host.Dispose();
            Log.CloseAndFlush();
        }

        private void InitializeRemoteDiagnostics(TelemetrySettings telemetrySettings)
        {
            RemoteDiagnosticsLifecycle.Initialize(
                Host.Services.GetRequiredService<IRemoteDiagnosticsService>(),
                telemetrySettings,
                Host.Services.GetRequiredService<TelemetryContext>());
        }

        private void ShutdownRemoteDiagnostics()
        {
            ILogger transportLogger = AppLogger.ForContext("PostHogTransportInternal", true);
            transportLogger.Debug("Flushing Foundry remote diagnostics.");
            using var cancellation = new CancellationTokenSource(RemoteDiagnosticsShutdownTimeout);
            try
            {
                RemoteDiagnosticsLifecycle.ShutdownAsync(
                    GetService<IRemoteDiagnosticsService>(),
                    cancellation.Token).GetAwaiter().GetResult();
                transportLogger.Debug("Foundry remote diagnostics flush completed.");
            }
            catch (OperationCanceledException)
            {
                transportLogger.Warning(
                    "Foundry remote diagnostics flush timed out after {TimeoutSeconds} seconds.",
                    RemoteDiagnosticsShutdownTimeout.TotalSeconds);
            }
            catch (Exception ex)
            {
                transportLogger.Warning(ex, "Foundry remote diagnostics shutdown failed.");
            }
        }

    }
}
