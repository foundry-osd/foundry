// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO;
using System.Text.Json;
using Foundry.Utilities.Diagnostics;
using Serilog;
using Serilog.Events;

namespace Foundry.Deploy.Services.Logging;

public sealed class DeploymentLogService : IDeploymentLogService
{
    private static ILogger Logger => Log.ForContext<DeploymentLogService>();

    public DeploymentLogSession Initialize(string rootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            throw new ArgumentException("A root path is required.", nameof(rootPath));
        }

        string normalizedRoot = rootPath.Trim();
        string logsDirectory = Path.Combine(normalizedRoot, "Logs", "Deployment");
        string stateDirectory = Path.Combine(normalizedRoot, "State", "Deployment");
        Directory.CreateDirectory(logsDirectory);
        Directory.CreateDirectory(stateDirectory);

        string stateFilePath = Path.Combine(stateDirectory, "deployment-state.json");

        Logger.Information(
            "Deployment log session initialized. RootPath={RootPath}, LogsDirectoryPath={LogsDirectoryPath}",
            normalizedRoot,
            logsDirectory);

        return new DeploymentLogSession
        {
            RootPath = normalizedRoot,
            LogsDirectoryPath = logsDirectory,
            StateDirectoryPath = stateDirectory,
            StateFilePath = stateFilePath
        };
    }

    public Task AppendAsync(
        DeploymentLogSession session,
        DeploymentLogLevel level,
        string message,
        CancellationToken cancellationToken = default)
    {
        LogEventLevel serilogLevel = MapLevel(level);
        try
        {
            Logger
                .ForContext("DeploymentRootPath", session.RootPath)
                .Write(
                    serilogLevel,
                    "{DeploymentMessage}",
                    LogValueSanitizer.NormalizePropertyValue(message));
        }
        catch (Exception ex)
        {
            global::System.Diagnostics.Debug.WriteLine(
                $"Foundry.Deploy session log write failed: {ex.GetType().Name}");
        }

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

        string temporaryStateFilePath = session.StateFilePath + $".{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllTextAsync(temporaryStateFilePath, json, cancellationToken).ConfigureAwait(false);
            File.Move(temporaryStateFilePath, session.StateFilePath, overwrite: true);
        }
        finally
        {
            try
            {
                File.Delete(temporaryStateFilePath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                global::System.Diagnostics.Debug.WriteLine(
                    $"Foundry.Deploy temporary state cleanup failed: {ex.GetType().Name}");
            }
        }
    }

    internal static string[] EnumerateSupportFiles(IEnumerable<string?> directories, string? bootstrapSessionDirectory = null)
    {
        HashSet<string> files = new(StringComparer.OrdinalIgnoreCase);
        foreach (string? directory in directories)
        {
            if (string.IsNullOrWhiteSpace(directory)) continue;
            string root = Path.GetFullPath(directory);
            if (Path.GetFileName(root).Equals("Deployment", StringComparison.OrdinalIgnoreCase) &&
                (Path.GetFileName(Path.GetDirectoryName(root)) ?? string.Empty).Equals("Logs", StringComparison.OrdinalIgnoreCase))
                root = Path.GetDirectoryName(root)!;
            Collect(root, recursive: Path.GetFileName(root).Equals("Logs", StringComparison.OrdinalIgnoreCase));
        }
        if (!string.IsNullOrWhiteSpace(bootstrapSessionDirectory) && Directory.Exists(bootstrapSessionDirectory) &&
            (File.GetAttributes(bootstrapSessionDirectory) & FileAttributes.ReparsePoint) == 0)
        {
            Collect(bootstrapSessionDirectory, recursive: false);
            Collect(Path.Combine(bootstrapSessionDirectory, "Startup"), recursive: true);
        }
        return files.Order(StringComparer.OrdinalIgnoreCase).ToArray();

        void Collect(string directory, bool recursive)
        {
            if (!Directory.Exists(directory) || Path.GetFileName(directory).Equals("PendingLogs", StringComparison.OrdinalIgnoreCase) ||
                (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) return;
            foreach (string path in Directory.EnumerateFiles(directory))
            {
                string name = Path.GetFileName(path);
                string extension = Path.GetExtension(path);
                if (!name.StartsWith(".snapshot", StringComparison.OrdinalIgnoreCase) &&
                    (extension.Equals(".log", StringComparison.OrdinalIgnoreCase) ||
                     (recursive && (extension.Equals(".json", StringComparison.OrdinalIgnoreCase) ||
                                    extension.Equals(".txt", StringComparison.OrdinalIgnoreCase)))) &&
                    (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0)
                    files.Add(path);
            }
            if (recursive)
                foreach (string child in Directory.EnumerateDirectories(directory)) Collect(child, recursive: true);
        }
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
