using System.Collections.Concurrent;
using System.IO;
using System.Text.Json;
using Serilog;
using Serilog.Events;

namespace Foundry.Deploy.Services.Logging;

public sealed class DeploymentLogService : IDeploymentLogService, IDisposable
{
    private readonly ConcurrentDictionary<string, ILogger> _sessionLoggers = new(StringComparer.OrdinalIgnoreCase);

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

        string logFilePath = Path.Combine(logsDirectory, FoundryDeployLogging.LogFileName);
        string stateFilePath = Path.Combine(stateDirectory, "deployment-state.json");

        GetOrCreateSessionLogger(logFilePath)
            .Write(LogEventLevel.Information, "Log session initialized at {RootPath}.", normalizedRoot);

        return new DeploymentLogSession
        {
            RootPath = normalizedRoot,
            LogsDirectoryPath = logsDirectory,
            StateDirectoryPath = stateDirectory,
            LogFilePath = logFilePath,
            StateFilePath = stateFilePath
        };
    }

    public Task AppendAsync(
        DeploymentLogSession session,
        DeploymentLogLevel level,
        string message,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        LogEventLevel serilogLevel = MapLevel(level);

        GetOrCreateSessionLogger(session.LogFilePath)
            .Write(serilogLevel, "{LogMessage}", message);

        return Task.CompletedTask;
    }

    public async Task SaveStateAsync<TState>(
        DeploymentLogSession session,
        TState state,
        CancellationToken cancellationToken = default)
    {
        string json = JsonSerializer.Serialize(state, new JsonSerializerOptions
        {
            WriteIndented = true
        });

        await File.WriteAllTextAsync(session.StateFilePath, json, cancellationToken).ConfigureAwait(false);
    }

    public void Release(DeploymentLogSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (_sessionLoggers.TryRemove(session.LogFilePath, out ILogger? logger))
        {
            (logger as IDisposable)?.Dispose();
        }
    }

    public void Dispose()
    {
        foreach (ILogger logger in _sessionLoggers.Values)
        {
            (logger as IDisposable)?.Dispose();
        }

        _sessionLoggers.Clear();
    }

    private ILogger GetOrCreateSessionLogger(string logFilePath)
    {
        return _sessionLoggers.GetOrAdd(logFilePath, static path => FoundryDeployLogging.CreateLogger(path));
    }

    private static LogEventLevel MapLevel(DeploymentLogLevel level)
    {
        return level switch
        {
            DeploymentLogLevel.Verbose => LogEventLevel.Verbose,
            DeploymentLogLevel.Debug => LogEventLevel.Debug,
            DeploymentLogLevel.Info => LogEventLevel.Information,
            DeploymentLogLevel.Warning => LogEventLevel.Warning,
            DeploymentLogLevel.Error => LogEventLevel.Error,
            DeploymentLogLevel.Fatal => LogEventLevel.Fatal,
            _ => LogEventLevel.Information
        };
    }
}
