using System.Text.Json;
using System.IO;

namespace Foundry.Deploy.Services.Logging;

public sealed class DeploymentLogService : IDeploymentLogService
{
    public DeploymentLogSession Initialize(string rootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            throw new ArgumentException("A root path is required.", nameof(rootPath));
        }

        string normalizedRoot = rootPath.Trim();
        string logsDirectory = Path.Combine(normalizedRoot, "Logs");
        string stateDirectory = Path.Combine(normalizedRoot, "State");
        Directory.CreateDirectory(logsDirectory);
        Directory.CreateDirectory(stateDirectory);

        string timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss");
        string logFilePath = Path.Combine(logsDirectory, $"foundry-deploy-{timestamp}.log");
        string stateFilePath = Path.Combine(stateDirectory, "deployment-state.json");

        File.WriteAllText(logFilePath, $"[{DateTimeOffset.UtcNow:O}] [Info] Log session created.{Environment.NewLine}");

        return new DeploymentLogSession
        {
            RootPath = normalizedRoot,
            LogsDirectoryPath = logsDirectory,
            StateDirectoryPath = stateDirectory,
            LogFilePath = logFilePath,
            StateFilePath = stateFilePath
        };
    }

    public async Task AppendAsync(DeploymentLogSession session, DeploymentLogLevel level, string message, CancellationToken cancellationToken = default)
    {
        string line = $"[{DateTimeOffset.UtcNow:O}] [{level}] {message}{Environment.NewLine}";
        await File.AppendAllTextAsync(session.LogFilePath, line, cancellationToken).ConfigureAwait(false);
    }

    public async Task SaveStateAsync<TState>(DeploymentLogSession session, TState state, CancellationToken cancellationToken = default)
    {
        string json = JsonSerializer.Serialize(state, new JsonSerializerOptions
        {
            WriteIndented = true
        });

        await File.WriteAllTextAsync(session.StateFilePath, json, cancellationToken).ConfigureAwait(false);
    }
}
