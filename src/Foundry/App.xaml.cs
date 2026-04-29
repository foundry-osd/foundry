using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Serilog;

namespace Foundry;

public partial class App : Application
{
    private readonly IServiceProvider _services;

    public App(IServiceProvider services)
    {
        _services = services;
        InitializeComponent();
        UnhandledException += OnUnhandledException;
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            Log.Information("Launching Foundry WinUI main window.");
            MainWindow mainWindow = _services.GetRequiredService<MainWindow>();
            mainWindow.Activate();
            Log.Information("Foundry WinUI main window activated.");
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Failed to launch Foundry WinUI main window.");
            Log.CloseAndFlush();
            throw;
        }
    }

    private static void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs args)
    {
        Log.Fatal(args.Exception, "Unhandled WinUI exception.");
        Log.CloseAndFlush();
    }
}
