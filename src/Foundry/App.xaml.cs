namespace Foundry
{
    public partial class App : Application
    {
        public new static App Current => (App)Application.Current;
        public static Window MainWindow = Window.Current;
        public static IntPtr Hwnd => WinRT.Interop.WindowNative.GetWindowHandle(MainWindow);
        public IServiceProvider Services { get; }
        public IJsonNavigationService NavService => GetService<IJsonNavigationService>();
        public IThemeService ThemeService => GetService<IThemeService>();

        public static T GetService<T>() where T : class
        {
            if ((App.Current as App)!.Services.GetService(typeof(T)) is not T service)
            {
                throw new ArgumentException($"{typeof(T)} needs to be registered in ConfigureServices within App.xaml.cs.");
            }

            return service;
        }

        public App()
        {
            Services = ConfigureServices();
            this.InitializeComponent();
        }

        private static IServiceProvider ConfigureServices()
        {
            var services = new ServiceCollection();
            services.AddSingleton<IThemeService, ThemeService>();
            services.AddSingleton<IJsonNavigationService, JsonNavigationService>();
            services.AddSingleton<Foundry.Core.Services.Application.IApplicationLifetimeService, Services.Application.WinUiApplicationLifetimeService>();
            services.AddSingleton<Foundry.Core.Services.Application.IAppDispatcher, Services.Application.WinUiAppDispatcher>();
            services.AddSingleton<Foundry.Core.Services.Application.IDialogService, Services.Application.WinUiDialogService>();
            services.AddSingleton<Foundry.Core.Services.Application.IExternalProcessLauncher, Services.Application.WinUiExternalProcessLauncher>();
            services.AddSingleton<Foundry.Core.Services.Application.IFilePickerService, Services.Application.WinUiFilePickerService>();

            services.AddTransient<MainViewModel>();
            services.AddSingleton<ContextMenuService>();
            services.AddTransient<GeneralSettingViewModel>();
            services.AddTransient<AppUpdateSettingViewModel>();
            services.AddTransient<AboutUsSettingViewModel>();

            return services.BuildServiceProvider();
        }

        protected override void OnLaunched(LaunchActivatedEventArgs args)
        {
            MainWindow = new MainWindow();

            MainWindow.Title = MainWindow.AppWindow.Title = ProcessInfoHelper.ProductNameAndVersion;
            MainWindow.AppWindow.SetIcon("Assets/AppIcon.ico");

            ThemeService.Initialize(MainWindow);

            MainWindow.Activate();

            InitializeApp();
        }

        private async void InitializeApp()
        {
            var menuService = GetService<ContextMenuService>();
            if (menuService != null && RuntimeHelper.IsPackaged())
            {
                ContextMenuItem menu = new ContextMenuItem
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

            if (Settings.UseDeveloperMode)
            {
                ConfigureLogger();
            }

            UnhandledException += (s, e) => Logger?.Error(e.Exception, "UnhandledException");
        }
    }

}
