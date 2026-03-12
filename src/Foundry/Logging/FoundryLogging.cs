using Serilog;

namespace Foundry.Logging;

internal static class FoundryLogging
{
    private const string OutputTemplate =
        "{UtcTimestamp:yyyy-MM-dd HH:mm:ss} UTC | {Level:u3} | {SourceContext} | {Message:lj}{NewLine}{Exception}";
    private const string LogFileName = "Foundry.log";

    public static ILogger CreateApplicationLogger()
    {
        string logsDirectoryPath = GetLogsDirectoryPath();
        Directory.CreateDirectory(logsDirectoryPath);
        string filePath = Path.Combine(logsDirectoryPath, LogFileName);

        return new LoggerConfiguration()
            .MinimumLevel.Verbose()
            .Enrich.With(new UtcTimestampEnricher())
            .Enrich.FromLogContext()
            .WriteTo.File(filePath, outputTemplate: OutputTemplate, shared: true)
            .WriteTo.Debug(outputTemplate: OutputTemplate)
            .CreateLogger();
    }

    public static string GetLogsDirectoryPath()
    {
        string localAppDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrWhiteSpace(localAppDataPath))
        {
            return Path.Combine(localAppDataPath, "Foundry", "Logs");
        }

        return Path.Combine(Path.GetTempPath(), "Foundry", "Logs");
    }
}

