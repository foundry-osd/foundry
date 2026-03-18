using System.Diagnostics;
using System.Windows.Threading;
using Foundry.DependencyInjection;
using Foundry.Logging;
using Foundry.Services.Adk;
using Foundry.Services.WinPe;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;

namespace Foundry;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        Log.Logger = FoundryLogging.CreateApplicationLogger();
        RegisterGlobalExceptionHandlers();

        try
        {
            ConfigureLocalWinPeDeployForDebugSession();

            using IHost host = BuildHost(args);

            App app = host.Services.GetRequiredService<App>();
            app.DispatcherUnhandledException += OnDispatcherUnhandledException;
            app.InitializeComponent();

            MainWindow mainWindow = host.Services.GetRequiredService<MainWindow>();
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

    private static IHost BuildHost(string[] args)
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

        builder.Logging.ClearProviders();
        builder.Logging.AddSerilog(dispose: false);

        builder.Services.AddFoundryApplicationServices();

        return builder.Build();
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
