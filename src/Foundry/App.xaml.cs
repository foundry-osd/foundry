using Foundry.DependencyInjection;
using Foundry.Services.Localization;
using Foundry.Services.Settings;
using Foundry.Services.Startup;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace Foundry
{
    public partial class App : Application
    {
        private static readonly ILogger AppLogger = Log.ForContext<App>();
        private bool isShuttingDown;

        public new static App Current => (App)Application.Current;
        public static Window MainWindow = Window.Current;
        public static IntPtr Hwnd => WinRT.Interop.WindowNative.GetWindowHandle(MainWindow);
        public IHost Host { get; }
        public IServiceProvider Services => Host.Services;
        public IJsonNavigationService NavService => GetService<IJsonNavigationService>();
        public IThemeService ThemeService => GetService<IThemeService>();

        public static T GetService<T>() where T : class
        {
            if (Current.Services.GetService(typeof(T)) is not T service)
            {
                throw new ArgumentException($"{typeof(T)} needs to be registered in {nameof(ServiceCollectionExtensions)}.");
            }

            return service;
        }

        public App()
        {
            Host = FoundryHost.Create();
            SetDeveloperModeEnabled(Host.Services.GetRequiredService<IAppSettingsService>().Current.Diagnostics.DeveloperMode);
            Host.Services.GetRequiredService<IApplicationLocalizationService>().InitializeAsync().GetAwaiter().GetResult();
            RegisterWinUiExceptionHandler();

            AppLogger.Information("Foundry WinUI host initialized.");
            this.InitializeComponent();
        }

        protected override async void OnLaunched(LaunchActivatedEventArgs args)
        {
            try
            {
                MainWindow = GetService<MainWindow>();
                MainWindow.Closed += OnMainWindowClosed;

                MainWindow.Title = MainWindow.AppWindow.Title = ProcessInfoHelper.ProductNameAndVersion;
                MainWindow.AppWindow.SetIcon("Assets/AppIcon.ico");

                ThemeService.Initialize(MainWindow);

                MainWindow.Activate();

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
            ContextMenuService menuService = GetService<ContextMenuService>();
            if (RuntimeHelper.IsPackaged())
            {
                try
                {
                    ContextMenuItem menu = new()
                    {
                        Title = "Open Foundry Here",
                        Param = @"""{path}""",
                        AcceptFileFlag = (int)FileMatchFlagEnum.All,
                        AcceptDirectoryFlag = (int)(DirectoryMatchFlagEnum.Directory | DirectoryMatchFlagEnum.Background | DirectoryMatchFlagEnum.Desktop),
                        AcceptMultipleFilesFlag = (int)FilesMatchFlagEnum.Each,
                        Index = 0,
                        Enabled = true,
                        Icon = ProcessInfoHelper.GetFileVersionInfo().FileName,
                        Exe = "Foundry.exe"
                    };

                    await menuService.SaveAsync(menu);
                }
                catch (Exception ex)
                {
                    AppLogger.Warning(ex, "Failed to register packaged shell context menu.");
                }
            }

            await GetService<IStartupReadinessService>().InitializeAsync();
            AppLogger.Information("Foundry WinUI startup completed.");
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
            Host.Dispose();
            Log.CloseAndFlush();
        }
    }
}
