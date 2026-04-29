using System.Diagnostics;
using System.Runtime.InteropServices;
using Foundry.DependencyInjection;
using Foundry.Logging;
using Foundry.Services.Adk;
using Foundry.Services.Configuration;
using Foundry.Services.WinPe;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Velopack;

namespace Foundry;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        VelopackApp.Build().Run();

        Log.Logger = FoundryLogging.CreateApplicationLogger();
        RegisterGlobalExceptionHandlers();

        try
        {
            ConfigureLocalWinPeConnectForDebugSession();
            ConfigureLocalWinPeDeployForDebugSession();

            using IHost host = BuildHost(args);

            Log.Information("Starting Foundry WinUI application.");
            EnsureWindowsAppRuntimeLoaded();
            Log.Information("Windows App Runtime loaded.");
            WinRT.ComWrappersSupport.InitializeComWrappers();
            Microsoft.UI.Xaml.Application.Start(initialization =>
            {
                Log.Information("Initializing Foundry WinUI application instance.");
                var context = new Microsoft.UI.Dispatching.DispatcherQueueSynchronizationContext(
                    Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread());
                SynchronizationContext.SetSynchronizationContext(context);

                _ = ActivatorUtilities.CreateInstance<App>(host.Services, host.Services);
                Log.Information("Foundry WinUI application instance initialized.");
            });
            Log.Information("Foundry WinUI application exited.");
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

    private static void EnsureWindowsAppRuntimeLoaded()
    {
        Environment.SetEnvironmentVariable("MICROSOFT_WINDOWSAPPRUNTIME_BASE_DIRECTORY", AppContext.BaseDirectory);
        int result = WindowsAppRuntime_EnsureIsLoaded();
        if (result < 0)
        {
            Marshal.ThrowExceptionForHR(result);
        }
    }

    [DllImport("Microsoft.WindowsAppRuntime.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int WindowsAppRuntime_EnsureIsLoaded();

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

    private static void ConfigureLocalWinPeConnectForDebugSession()
    {
#if DEBUG
        if (!Debugger.IsAttached)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(WinPeDefaults.LocalConnectEnableEnvironmentVariable)))
        {
            Environment.SetEnvironmentVariable(WinPeDefaults.LocalConnectEnableEnvironmentVariable, "1");
        }

        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(WinPeDefaults.LocalConnectProjectEnvironmentVariable)))
        {
            return;
        }

        if (!TryFindProjectPath("Foundry.Connect", out string projectPath))
        {
            return;
        }

        Environment.SetEnvironmentVariable(WinPeDefaults.LocalConnectProjectEnvironmentVariable, projectPath);
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

    private static bool TryFindFoundryDeployProjectPath(out string projectPath)
    {
        return TryFindProjectPath("Foundry.Deploy", out projectPath);
    }

    private static bool TryFindProjectPath(string projectDirectoryName, out string projectPath)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            string candidate = Path.Combine(current.FullName, "src", projectDirectoryName, $"{projectDirectoryName}.csproj");
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
