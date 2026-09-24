// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO;
using Foundry.Telemetry;
using Foundry.Utilities.IO;
using Foundry.Utilities.Diagnostics;
using Serilog;
using Serilog.Events;

namespace Foundry.Deploy.Services.Logging;

internal static class FoundryDeployLogging
{
    public const string LogFileName = "FoundryDeploy.log";

    private const int RetainedLogFileCount = 5;
    private static readonly object PersistenceSync = new();
    private static string? _startupLogDirectoryPath;
    private static string? _persistenceDirectoryPath;

    public static string CurrentLogFilePath { get; private set; } = "<unavailable>";

    public static string ResolveStartupLogFilePath()
    {
        string[] candidateDirectories =
        [
            @"X:\Foundry\Logs",
            Path.Combine(Path.GetTempPath(), "Foundry", "Logs"),
            AppContext.BaseDirectory
        ];

        return WritableFilePathResolver.Resolve(candidateDirectories, LogFileName);
    }

    public static ILogger CreateLogger(string logFilePath)
    {
        string normalizedLogFilePath = Path.GetFullPath(logFilePath);
        lock (PersistenceSync)
        {
            RemoteDiagnosticsSink.SetLogDirectory(Path.Combine(Path.GetDirectoryName(normalizedLogFilePath)!, "PendingLogs"));
            ILogger logger = FoundryLogConfiguration.CreateFileLogger(
                logFilePath,
                "Foundry.Deploy",
                DiagnosticSessionContext.CurrentSessionId,
                LogEventLevel.Verbose,
                RetainedLogFileCount,
                additionalSink: RemoteDiagnosticsSink.Instance);
            CurrentLogFilePath = normalizedLogFilePath;
            _startupLogDirectoryPath = Path.GetDirectoryName(normalizedLogFilePath);
            return logger;
        }
    }

    public static LogPersistenceResult PersistCurrentLogs()
    {
        lock (PersistenceSync)
        {
            return PersistCurrentLogsCore();
        }
    }

    private static LogPersistenceResult PersistCurrentLogsCore()
    {
        string? sourceDirectoryPath = _startupLogDirectoryPath;
        if (string.IsNullOrWhiteSpace(sourceDirectoryPath))
        {
            return new LogPersistenceResult(0, 0);
        }

        string[] targetDirectoryPaths =
        [
            .. new[]
            {
                _persistenceDirectoryPath,
                Environment.GetEnvironmentVariable(DiagnosticSessionContext.PersistenceDirectoryEnvironmentVariableName)
            }
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Select(static path => Path.GetFullPath(path!))
            .Distinct(StringComparer.OrdinalIgnoreCase)
        ];

        int copiedFileCount = 0;
        int failedFileCount = 0;
        foreach (string targetDirectoryPath in targetDirectoryPaths)
        {
            LogPersistenceResult result = PersistLogSnapshot(sourceDirectoryPath, targetDirectoryPath);
            copiedFileCount += result.CopiedFileCount;
            failedFileCount += result.FailedFileCount;
        }

        return new LogPersistenceResult(copiedFileCount, failedFileCount);
    }

    internal static LogPersistenceResult PersistLogSnapshot(
        string sourceDirectoryPath,
        string targetDirectoryPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceDirectoryPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetDirectoryPath);

        string normalizedSource = Path.GetFullPath(sourceDirectoryPath);
        string normalizedTarget = Path.GetFullPath(targetDirectoryPath);
        if (normalizedSource.Equals(normalizedTarget, StringComparison.OrdinalIgnoreCase) ||
            !Directory.Exists(normalizedSource))
        {
            return new LogPersistenceResult(0, 0);
        }

        lock (PersistenceSync)
        {
            try
            {
                int copied = DiagnosticLogSnapshot.CopyAsync(normalizedSource, normalizedTarget, "*.log")
                    .GetAwaiter().GetResult();
                return new LogPersistenceResult(copied, 0);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                global::System.Diagnostics.Debug.WriteLine(
                    $"Foundry.Deploy log snapshot failed: {ex.GetType().Name}");
                return new LogPersistenceResult(0, 1);
            }
        }
    }

    internal static void SwitchPersistenceDirectory(string sourceRoot, string destinationDirectory)
    {
        lock (PersistenceSync)
        {
            string? inherited = Environment.GetEnvironmentVariable(DiagnosticSessionContext.PersistenceDirectoryEnvironmentVariableName);
            if (!string.IsNullOrWhiteSpace(inherited) && IsWithinRoot(inherited, sourceRoot))
                Environment.SetEnvironmentVariable(DiagnosticSessionContext.PersistenceDirectoryEnvironmentVariableName, destinationDirectory);
            _persistenceDirectoryPath = Path.GetFullPath(destinationDirectory);
        }
    }

    internal static bool CanRetireRoot(string root)
    {
        lock (PersistenceSync)
        {
            return (string.IsNullOrWhiteSpace(_startupLogDirectoryPath) || !IsWithinRoot(_startupLogDirectoryPath, root)) &&
                (!Directory.Exists(root) || !Directory.EnumerateDirectories(root, "PendingLogs", SearchOption.AllDirectories).Any());
        }
    }

    internal static bool TryRetireRoot(string root, string destinationDirectory)
    {
        lock (PersistenceSync)
        {
            if (!CanRetireRoot(root)) return false;
            SwitchPersistenceDirectory(root, destinationDirectory);
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
            return true;
        }
    }

    private static bool IsWithinRoot(string path, string root)
    {
        string normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        string normalizedPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        return normalizedPath.Equals(normalizedRoot, StringComparison.OrdinalIgnoreCase) ||
            normalizedPath.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
}
