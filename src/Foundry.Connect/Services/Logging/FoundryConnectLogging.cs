using System.IO;
using Foundry.Connect.Services.Runtime;
using Serilog;

namespace Foundry.Connect.Services.Logging;

internal static class FoundryConnectLogging
{
    public const string LogFileName = "FoundryConnect.log";

    private const string OutputTemplate =
        "{UtcTimestamp:yyyy-MM-dd HH:mm:ss} UTC | {Level:u3} | {SourceContext} | {Message:lj}{NewLine}{Exception}";

    public static string ResolveStartupLogFilePath()
    {
        foreach (string candidateDirectory in ConnectWorkspacePaths.EnumerateStartupLogDirectories())
        {
            if (string.IsNullOrWhiteSpace(candidateDirectory))
            {
                continue;
            }

            try
            {
                Directory.CreateDirectory(candidateDirectory);
                return Path.Combine(candidateDirectory, LogFileName);
            }
            catch
            {
                // Try the next candidate.
            }
        }

        return LogFileName;
    }

    public static ILogger CreateLogger(string logFilePath)
    {
        return new LoggerConfiguration()
            .MinimumLevel.Verbose()
            .Enrich.With(new UtcTimestampEnricher())
            .Enrich.FromLogContext()
            .WriteTo.File(logFilePath, outputTemplate: OutputTemplate, shared: true)
            .WriteTo.Debug(outputTemplate: OutputTemplate)
            .CreateLogger();
    }
}
