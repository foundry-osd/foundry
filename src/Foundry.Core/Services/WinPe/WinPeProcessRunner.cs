// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using Foundry.Telemetry;
using Foundry.Utilities.Processes;
using Serilog;
using Serilog.Events;
using UtilityProcessRunner = Foundry.Utilities.Processes.ProcessRunner;

namespace Foundry.Core.Services.WinPe;

public sealed class WinPeProcessRunner : IWinPeProcessOutputRunner
{
    private const string InternalSetEnvKey = "FOUNDRY_ADK_SETENV_PATH";
    private readonly UtilityProcessRunner _processRunner = new();

    public async Task<WinPeProcessExecution> RunAsync(
        string fileName,
        string arguments,
        string workingDirectory,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string>? environmentOverrides = null)
    {
        return await RunWithOutputAsync(
            fileName,
            arguments,
            workingDirectory,
            null,
            null,
            cancellationToken,
            environmentOverrides).ConfigureAwait(false);
    }

    public Task<WinPeProcessExecution> RunWithOutputAsync(
        string fileName,
        string arguments,
        string workingDirectory,
        Action<string>? onOutputData,
        Action<string>? onErrorData,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string>? environmentOverrides = null)
    {
        return RunWithOutputCoreAsync(fileName, arguments, workingDirectory, onOutputData, onErrorData,
            cancellationToken, environmentOverrides, isMakeWinPeMediaHelp: false);
    }

    private async Task<WinPeProcessExecution> RunWithOutputCoreAsync(
        string fileName,
        string arguments,
        string workingDirectory,
        Action<string>? onOutputData,
        Action<string>? onErrorData,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string>? environmentOverrides,
        bool isMakeWinPeMediaHelp)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);

        ProcessExecutionRequest request = ProcessExecutionRequest.FromRawArguments(
            fileName,
            arguments,
            workingDirectory) with
        {
            EnvironmentOverrides = FilterEnvironmentOverrides(environmentOverrides),
            OnOutputData = onOutputData,
            OnErrorData = onErrorData
        };

        Stopwatch stopwatch = Stopwatch.StartNew();
        try
        {
            ProcessExecutionResult result = await _processRunner
                .RunAsync(request, cancellationToken)
                .ConfigureAwait(false);
            if (!result.IsSuccess)
            {
                ILogger logger = Log.ForContext<WinPeProcessRunner>();
                foreach ((string name, object value) in RemoteProcessDiagnostics.CreateProperties(result, stopwatch.Elapsed))
                {
                    logger = logger.ForContext(name, value);
                }
                LogEventLevel level = IsExpectedNonzeroExit(result, isMakeWinPeMediaHelp)
                    ? LogEventLevel.Debug
                    : LogEventLevel.Warning;
                logger.Write(level, "External process returned a nonzero exit code. ToolName={ToolName}, ExitCode={ExitCode}",
                    Path.GetFileName(result.FileName), result.ExitCode);
            }
            return WinPeProcessExecution.FromProcessExecutionResult(result);
        }
        catch (ProcessStartException ex)
        {
            ILogger logger = Log.ForContext<WinPeProcessRunner>();
            foreach ((string name, object value) in RemoteProcessDiagnostics.CreateStartFailureProperties(ex, stopwatch.Elapsed))
            {
                logger = logger.ForContext(name, value);
            }
            logger.Warning("External process could not be started.");
            if (ex.InnerException is Win32Exception or InvalidOperationException)
            {
                ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            }
            throw new InvalidOperationException($"Failed to start process '{fileName}'.", ex);
        }
    }

    public Task<WinPeProcessExecution> RunCmdScriptAsync(
        string scriptPath,
        string scriptArguments,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        return RunCmdScriptCoreAsync(
            scriptPath,
            scriptArguments,
            workingDirectory,
            cancellationToken,
            callTargetScript: true,
            useCommandExtensionsStripQuoteRules: true);
    }

    public Task<WinPeProcessExecution> RunCmdScriptDirectAsync(
        string scriptPath,
        string scriptArguments,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        return RunCmdScriptCoreAsync(
            scriptPath,
            scriptArguments,
            workingDirectory,
            cancellationToken,
            callTargetScript: false,
            useCommandExtensionsStripQuoteRules: false);
    }

    public static string Quote(string value)
    {
        return value.Contains(' ', StringComparison.Ordinal)
            ? $"\"{value}\""
            : value;
    }

    private Task<WinPeProcessExecution> RunCmdScriptCoreAsync(
        string scriptPath,
        string scriptArguments,
        string workingDirectory,
        CancellationToken cancellationToken,
        bool callTargetScript,
        bool useCommandExtensionsStripQuoteRules)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scriptPath);

        string normalizedScriptArguments = string.IsNullOrWhiteSpace(scriptArguments)
            ? string.Empty
            : $" {scriptArguments}";

        string scriptCommand = $"{Quote(scriptPath)}{normalizedScriptArguments}";
        string command = callTargetScript
            ? $"call {scriptCommand}"
            : scriptCommand;

        IReadOnlyDictionary<string, string>? environmentOverrides = BuildAdkEnvironmentOverrides(scriptPath);
        if (environmentOverrides is not null &&
            environmentOverrides.TryGetValue(InternalSetEnvKey, out string? setEnvPath) &&
            !string.IsNullOrWhiteSpace(setEnvPath))
        {
            command = $"call {Quote(setEnvPath)} >nul 2>&1 && {command}";
        }

        string switchS = useCommandExtensionsStripQuoteRules ? " /s" : string.Empty;
        string arguments = $"/d{switchS} /c \"{command}\"";
        bool isMakeWinPeMediaHelp = Path.GetFileName(scriptPath).Equals("MakeWinPEMedia.cmd", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(scriptArguments?.Trim(), "/?", StringComparison.Ordinal);
        return RunWithOutputCoreAsync(GetCommandProcessorPath(), arguments, workingDirectory, null, null,
            cancellationToken, environmentOverrides, isMakeWinPeMediaHelp);
    }

    /// <summary>
    /// Classifies known nonzero outcomes for logging only; callers retain the raw exit code and output.
    /// </summary>
    private static bool IsExpectedNonzeroExit(ProcessExecutionResult result, bool isMakeWinPeMediaHelp)
    {
        if (Path.GetFileName(result.FileName).Equals("robocopy.exe", StringComparison.OrdinalIgnoreCase))
        {
            return WinPeUsbMediaService.IsRobocopySuccessExitCode(result.ExitCode);
        }

        // MakeWinPEMedia help can exit with 1 even when the capability probe has usable output.
        return isMakeWinPeMediaHelp && result.ExitCode == 1 &&
            (result.StandardOutput.Contains("/bootex", StringComparison.OrdinalIgnoreCase) ||
             result.StandardError.Contains("/bootex", StringComparison.OrdinalIgnoreCase));
    }

    private static string GetCommandProcessorPath()
    {
        string? cmdPath = Environment.GetEnvironmentVariable("ComSpec");
        if (!string.IsNullOrWhiteSpace(cmdPath))
        {
            return cmdPath;
        }

        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "cmd.exe");
    }

    private static IReadOnlyDictionary<string, string?>? FilterEnvironmentOverrides(
        IReadOnlyDictionary<string, string>? environmentOverrides)
    {
        if (environmentOverrides is null)
        {
            return null;
        }

        var filtered = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach ((string key, string value) in environmentOverrides)
        {
            if (!key.StartsWith("FOUNDRY_", StringComparison.Ordinal))
            {
                filtered[key] = value;
            }
        }

        return filtered;
    }

    private static IReadOnlyDictionary<string, string>? BuildAdkEnvironmentOverrides(string scriptPath)
    {
        string? winPeRoot = FindWinPeRootDirectory(scriptPath);
        if (winPeRoot is null)
        {
            return null;
        }

        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["WinPERoot"] = winPeRoot
        };

        string? adkRoot = Directory.GetParent(winPeRoot)?.FullName;
        if (string.IsNullOrWhiteSpace(adkRoot))
        {
            return result;
        }

        string deploymentToolsRoot = Path.Combine(adkRoot, "Deployment Tools");
        if (!Directory.Exists(deploymentToolsRoot))
        {
            return result;
        }

        string[] hostArchitectureCandidates = Environment.Is64BitOperatingSystem
            ? ["amd64", "x86"]
            : ["x86", "amd64"];

        foreach (string hostArchitecture in hostArchitectureCandidates)
        {
            string hostToolsRoot = Path.Combine(deploymentToolsRoot, hostArchitecture);
            if (!Directory.Exists(hostToolsRoot))
            {
                continue;
            }

            string oscdimgRoot = Path.Combine(hostToolsRoot, "Oscdimg");
            if (Directory.Exists(oscdimgRoot))
            {
                result["OSCDImgRoot"] = oscdimgRoot;
            }

            string dismRoot = Path.Combine(hostToolsRoot, "DISM");
            if (Directory.Exists(dismRoot))
            {
                result["DISMRoot"] = dismRoot;
            }

            break;
        }

        string setEnvPath = Path.Combine(deploymentToolsRoot, "DandISetEnv.bat");
        if (File.Exists(setEnvPath))
        {
            result[InternalSetEnvKey] = setEnvPath;
        }

        return result;
    }

    private static string? FindWinPeRootDirectory(string scriptPath)
    {
        string? directoryPath = Path.GetDirectoryName(scriptPath);
        if (string.IsNullOrWhiteSpace(directoryPath))
        {
            return null;
        }

        var current = new DirectoryInfo(directoryPath);
        while (current is not null)
        {
            if (current.Name.Equals("Windows Preinstallation Environment", StringComparison.OrdinalIgnoreCase))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        return null;
    }
}
