// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.DependencyInjection;
using Foundry.Services.Configuration;
using Foundry.Services.Application;
using Foundry.Services.Appearance;
using Foundry.Services.Localization;
using Foundry.Services.Settings;
using Foundry.Services.Shell;
using Foundry.Services.Startup;
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
        private bool isShuttingDown;

        /// <summary>
        /// Gets the active Foundry application instance.
        /// </summary>
        public new static App Current => (App)Application.Current;

        /// <summary>
        /// Gets the main window once the application has launched.
        /// </summary>
        public static Window MainWindow = Window.Current;

        /// <summary>
        /// Gets the native window handle used by WinUI-backed platform services.
        /// </summary>
        public static IntPtr Hwnd => WinRT.Interop.WindowNative.GetWindowHandle(MainWindow);

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
            SetDeveloperModeEnabled(Host.Services.GetRequiredService<IAppSettingsService>().Current.Diagnostics.DeveloperMode);
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

                mainWindow.Title = mainWindow.AppWindow.Title = ApplicationInfo.ProductNameAndVersion;
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
            await GetService<IStartupReadinessService>().InitializeAsync();
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
            await GetService<ITelemetryService>().TrackAsync(TelemetryEvents.AppDailyActive, new Dictionary<string, object?>());
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

        private void OnMainWindowClosed(object sender, WindowEventArgs args)
        {
            if (isShuttingDown)
            {
                return;
            }

            isShuttingDown = true;
            AppLogger.Information("Foundry WinUI shutdown started.");
            AppLogger.Debug("Flushing Foundry telemetry events.");
            GetService<ITelemetryService>().FlushAsync().GetAwaiter().GetResult();
            AppLogger.Debug("Foundry telemetry flush completed.");
            Host.Dispose();
            Log.CloseAndFlush();
        }

    }
}
