using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Serilog;
using System.Runtime.InteropServices;
using Velopack;

namespace Foundry;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        Constants.EnsureDataDirectories();
        ConfigureLogger();
        RegisterGlobalExceptionHandlers();

        ILogger logger = Log.ForContext(typeof(Program));
        logger.Information(
            "Foundry process bootstrap started. Version={Version}, RuntimeIdentifier={RuntimeIdentifier}, ProcessArchitecture={ProcessArchitecture}, ArgumentsCount={ArgumentsCount}",
            FoundryApplicationInfo.Version,
            RuntimeInformation.RuntimeIdentifier,
            RuntimeInformation.ProcessArchitecture.ToString(),
            args.Length);

        try
        {
            logger.Information("Velopack startup flow started.");
            VelopackApp.Build()
                .OnFirstRun(version => logger.Information("Velopack first run detected. Version={Version}", version))
                .OnRestarted(version => logger.Information("Velopack restarted Foundry after update. Version={Version}", version))
                .OnBeforeUpdateFastCallback(version =>
                {
                    logger.Information("Velopack before-update fast callback started. Version={Version}", version);
                    Log.CloseAndFlush();
                })
                .OnAfterUpdateFastCallback(version =>
                {
                    logger.Information("Velopack after-update fast callback completed. Version={Version}", version);
                    Log.CloseAndFlush();
                })
                .Run();
            logger.Information("Velopack startup flow completed.");

            logger.Information("WinRT COM wrapper initialization started.");
            WinRT.ComWrappersSupport.InitializeComWrappers();
            logger.Information("WinRT COM wrapper initialization completed.");

            Application.Start(_ =>
            {
                DispatcherQueueSynchronizationContext context = new(DispatcherQueue.GetForCurrentThread());
                SynchronizationContext.SetSynchronizationContext(context);
                new App();
            });
        }
        catch (Exception ex)
        {
            logger.Fatal(ex, "Foundry process bootstrap failed.");
            Log.CloseAndFlush();
            throw;
        }
    }
}
