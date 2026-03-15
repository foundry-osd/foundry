using System.Diagnostics;
using System.Windows.Threading;
using Foundry.Logging;
using Foundry.Services.Configuration;
using Foundry.Services.Adk;
using Foundry.Services.ApplicationShell;
using Foundry.Services.Localization;
using Foundry.Services.Operations;
using Foundry.Services.Theme;
using Foundry.Services.WinPe;
using Foundry.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;

namespace Foundry;

public static class Program
{
    [STAThread]
    public static void Main()
    {
        Log.Logger = FoundryLogging.CreateApplicationLogger();
        RegisterGlobalExceptionHandlers();

        try
        {
            ConfigureLocalWinPeDeployForDebugSession();

            using ServiceProvider serviceProvider = BuildServiceProvider();

            App app = serviceProvider.GetRequiredService<App>();
            app.DispatcherUnhandledException += OnDispatcherUnhandledException;
            app.InitializeComponent();

            MainWindow mainWindow = serviceProvider.GetRequiredService<MainWindow>();
            app.Run(mainWindow);
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Foundry terminated due to an unhandled startup/runtime exception.");
            throw;
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    private static ServiceProvider BuildServiceProvider()
    {
        ServiceCollection services = new();

        services.AddLogging(builder =>
        {
            builder.ClearProviders();
            builder.AddSerilog(dispose: false);
        });

        services.AddSingleton<App>();
        services.AddSingleton<MainWindow>();
        services.AddSingleton<MainWindowViewModel>();

        services.AddSingleton<IApplicationShellService, ApplicationShellService>();
        services.AddSingleton<IExpertConfigurationService, ExpertConfigurationService>();
        services.AddSingleton<IDeployConfigurationGenerator, DeployConfigurationGenerator>();
        services.AddSingleton<ILanguageRegistryService, EmbeddedLanguageRegistryService>();
        services.AddSingleton<IThemeService, ThemeService>();
        services.AddSingleton<ILocalizationService, LocalizationService>();
        services.AddSingleton<IOperationProgressService, OperationProgressService>();
        services.AddSingleton<IAdkService, AdkService>();
        services.AddSingleton<IWinPeBuildService, WinPeBuildService>();
        services.AddSingleton<IWinPeDriverCatalogService, WinPeDriverCatalogService>();
        services.AddSingleton<IWinPeDriverInjectionService, WinPeDriverInjectionService>();
        services.AddSingleton<IMediaOutputService, MediaOutputService>();

        return services.BuildServiceProvider();
    }

    private static void ConfigureLocalWinPeDeployForDebugSession()
    {
#if DEBUG
        if (!Debugger.IsAttached)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(WinPeDefaults.LocalDeployEnableEnvironmentVariable)))
        {
            Environment.SetEnvironmentVariable(WinPeDefaults.LocalDeployEnableEnvironmentVariable, "1");
        }

        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(WinPeDefaults.LocalDeployProjectEnvironmentVariable)))
        {
            return;
        }

        if (!TryFindFoundryDeployProjectPath(out string projectPath))
        {
            return;
        }

        Environment.SetEnvironmentVariable(WinPeDefaults.LocalDeployProjectEnvironmentVariable, projectPath);
#endif
    }

    private static void RegisterGlobalExceptionHandlers()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception exception)
            {
                Log.Fatal(exception, "Unhandled AppDomain exception (IsTerminating={IsTerminating}).", args.IsTerminating);
                return;
            }

            Log.Fatal("Unhandled AppDomain exception object (IsTerminating={IsTerminating}): {ExceptionObject}",
                args.IsTerminating,
                args.ExceptionObject);
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Error(args.Exception, "Unobserved task exception.");
            args.SetObserved();
        };
    }

    private static void OnDispatcherUnhandledException(object? sender, DispatcherUnhandledExceptionEventArgs args)
    {
        Log.Fatal(args.Exception, "Unhandled WPF dispatcher exception.");
    }

    private static bool TryFindFoundryDeployProjectPath(out string projectPath)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            string candidate = Path.Combine(current.FullName, "src", "Foundry.Deploy", "Foundry.Deploy.csproj");
            if (File.Exists(candidate))
            {
                projectPath = candidate;
                return true;
            }

            current = current.Parent;
        }

        projectPath = string.Empty;
        return false;
    }
}
