using Serilog;

namespace Foundry.Logging;

internal static class FoundryLogging
{
    private const int RetentionDays = 7;
    private const string OutputTemplate =
        "{UtcTimestamp:yyyy-MM-dd HH:mm:ss} UTC | {Level:u3} | {SourceContext} | {Message:lj}{NewLine}{Exception}";

    public static ILogger CreateApplicationLogger()
    {
        string logsDirectoryPath = ResolveLogsDirectoryPath();
        Directory.CreateDirectory(logsDirectoryPath);
        PurgeExpiredLogs(logsDirectoryPath);

        string filePath = Path.Combine(
            logsDirectoryPath,
            $"foundry-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.log");

        return new LoggerConfiguration()
            .MinimumLevel.Verbose()
            .Enrich.With(new UtcTimestampEnricher())
            .Enrich.FromLogContext()
            .WriteTo.File(filePath, outputTemplate: OutputTemplate, shared: true)
            .WriteTo.Debug(outputTemplate: OutputTemplate)
            .CreateLogger();
    }

    private static string ResolveLogsDirectoryPath()
    {
        string localAppDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrWhiteSpace(localAppDataPath))
        {
            return Path.Combine(localAppDataPath, "Foundry", "Logs");
        }

        return Path.Combine(Path.GetTempPath(), "Foundry", "Logs");
    }

    private static void PurgeExpiredLogs(string logsDirectoryPath)
    {
        DateTime cutoffUtc = DateTime.UtcNow.AddDays(-RetentionDays);

        foreach (string path in Directory.EnumerateFiles(logsDirectoryPath, "*.log", SearchOption.TopDirectoryOnly))
        {
            try
            {
                DateTime lastWriteUtc = File.GetLastWriteTimeUtc(path);
                if (lastWriteUtc < cutoffUtc)
                {
                    File.Delete(path);
                }
            }
            catch
            {
                // Best-effort retention cleanup.
            }
        }
    }
}

