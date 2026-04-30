using System.Runtime.InteropServices;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace Foundry.Common
{
    public static partial class LoggerSetup
    {
        public static ILogger Logger { get; private set; } = Serilog.Core.Logger.None;

        public static void ConfigureLogger()
        {
            Directory.CreateDirectory(Constants.LogDirectoryPath);

            Logger = new LoggerConfiguration()
                .MinimumLevel.Information()
                .Enrich.FromLogContext()
                .Enrich.With<UtcTimestampEnricher>()
                .Enrich.WithProperty("Application", Constants.ApplicationName)
                .Enrich.WithProperty("Version", FoundryApplicationInfo.Version)
                .Enrich.WithProperty("RuntimeIdentifier", RuntimeInformation.RuntimeIdentifier)
                .Enrich.WithProperty("ProcessArchitecture", RuntimeInformation.ProcessArchitecture.ToString())
                .Enrich.WithProperty("UpdateContext", "Startup")
                .WriteTo.File(Constants.LogFilePath, shared: true)
                .WriteTo.Debug()
                .CreateLogger();

            Log.Logger = Logger;
        }

        private sealed class UtcTimestampEnricher : ILogEventEnricher
        {
            public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
            {
                logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("UtcTimestamp", DateTimeOffset.UtcNow));
            }
        }
    }
}
