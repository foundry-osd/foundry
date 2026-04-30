using Foundry.DependencyInjection;
using Foundry.Services.Startup;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace Foundry
{
    public partial class App : Application
    {
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
            Constants.EnsureDataDirectories();
            ConfigureLogger();

            Host = FoundryHost.Create();
            RegisterExceptionHandlers();

            Logger.Information("Foundry WinUI startup initialized.");
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
                Logger.Fatal(ex, "Foundry failed during WinUI launch.");
                throw;
            }
        }

        private static async Task InitializeAppAsync()
        {
            ContextMenuService menuService = GetService<ContextMenuService>();
            if (RuntimeHelper.IsPackaged())
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

            await GetService<IStartupReadinessService>().InitializeAsync();
        }

        private void RegisterExceptionHandlers()
        {
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
            UnhandledException += OnWinUiUnhandledException;
        }

        private static void OnUnhandledException(object sender, System.UnhandledExceptionEventArgs e)
        {
            if (e.ExceptionObject is Exception ex)
            {
                Logger.Fatal(ex, "Unhandled AppDomain exception. IsTerminating={IsTerminating}", e.IsTerminating);
                return;
            }

            Logger.Fatal("Unhandled AppDomain exception. IsTerminating={IsTerminating}, ExceptionObject={ExceptionObject}", e.IsTerminating, e.ExceptionObject);
        }

        private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            Logger.Error(e.Exception, "Unobserved task exception.");
            e.SetObserved();
        }

        private static void OnWinUiUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
        {
            Logger.Fatal(e.Exception, "Unhandled WinUI exception.");
        }

        private void OnMainWindowClosed(object sender, WindowEventArgs args)
        {
            if (isShuttingDown)
            {
                return;
            }

            isShuttingDown = true;
            Logger.Information("Foundry WinUI shutdown started.");
            Host.Dispose();
            Log.CloseAndFlush();
        }
    }
}
