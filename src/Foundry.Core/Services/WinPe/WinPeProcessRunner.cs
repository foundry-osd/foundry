// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.ComponentModel;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using Foundry.Utilities.Processes;
using UtilityProcessRunner = Foundry.Utilities.Processes.ProcessRunner;

namespace Foundry.Core.Services.WinPe;

public sealed class WinPeProcessRunner : IWinPeProcessOutputRunner
{
    private static readonly TimeSpan DefaultExecutionTimeout = TimeSpan.FromHours(4);
    private readonly UtilityProcessRunner _processRunner = new();

    /// <inheritdoc />
    public Task<WinPeProcessExecution> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string>? environmentOverrides = null,
        TimeSpan? executionTimeout = null) =>
        RunWithOutputAsync(fileName, arguments, workingDirectory, null, null, cancellationToken, environmentOverrides, executionTimeout);

    public async Task<WinPeProcessExecution> RunAsync(
        string fileName,
        string arguments,
        string workingDirectory,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string>? environmentOverrides = null,
        TimeSpan? executionTimeout = null)
    {
        return await RunWithOutputAsync(
            fileName,
            arguments,
            workingDirectory,
            null,
            null,
            cancellationToken,
            environmentOverrides,
            executionTimeout).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<WinPeProcessExecution> RunWithOutputAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        Action<string>? onOutputData,
        Action<string>? onErrorData,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string>? environmentOverrides = null,
        TimeSpan? executionTimeout = null) =>
        RunCoreAsync(new ProcessExecutionRequest(fileName, arguments, workingDirectory), onOutputData, onErrorData, cancellationToken, environmentOverrides, executionTimeout);

    public Task<WinPeProcessExecution> RunWithOutputAsync(
        string fileName,
        string arguments,
        string workingDirectory,
        Action<string>? onOutputData,
        Action<string>? onErrorData,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string>? environmentOverrides = null,
        TimeSpan? executionTimeout = null) =>
        RunCoreAsync(ProcessExecutionRequest.FromRawArguments(fileName, arguments, workingDirectory), onOutputData, onErrorData, cancellationToken, environmentOverrides, executionTimeout);

    private async Task<WinPeProcessExecution> RunCoreAsync(
        ProcessExecutionRequest request,
        Action<string>? onOutputData,
        Action<string>? onErrorData,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string>? environmentOverrides,
        TimeSpan? executionTimeout)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.FileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.WorkingDirectory);

        request = request with
        {
            EnvironmentOverrides = FilterEnvironmentOverrides(environmentOverrides),
            OnOutputData = onOutputData,
            OnErrorData = onErrorData,
            ExecutionTimeout = executionTimeout ?? DefaultExecutionTimeout
        };

        try
        {
            ProcessExecutionResult result = await _processRunner
                .RunAsync(request, cancellationToken)
                .ConfigureAwait(false);
            Foundry.Core.Services.Diagnostics.DismDiagnosticScope.Record(request.FileName, result);
            return WinPeProcessExecution.FromProcessExecutionResult(result);
        }
        catch (ProcessStartException ex) when (ex.InnerException is Win32Exception or InvalidOperationException)
        {
            ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }
        catch (ProcessStartException ex)
        {
            throw new InvalidOperationException($"Failed to start process '{request.FileName}'.", ex);
        }
    }

    public Task<WinPeProcessExecution> RunCmdScriptAsync(
        string scriptPath,
        string scriptArguments,
        string workingDirectory,
        CancellationToken cancellationToken,
        TimeSpan? executionTimeout = null)
    {
        return RunCmdScriptCoreAsync(
            scriptPath,
            scriptArguments,
            workingDirectory,
            cancellationToken,
            callTargetScript: true,
            useCommandExtensionsStripQuoteRules: true,
            executionTimeout);
    }

    public Task<WinPeProcessExecution> RunCmdScriptDirectAsync(
        string scriptPath,
        string scriptArguments,
        string workingDirectory,
        CancellationToken cancellationToken,
        TimeSpan? executionTimeout = null)
    {
        return RunCmdScriptCoreAsync(
            scriptPath,
            scriptArguments,
            workingDirectory,
            cancellationToken,
            callTargetScript: false,
            useCommandExtensionsStripQuoteRules: false,
            executionTimeout);
    }

    /// <summary>Quotes a batch value; expansion and control syntax are unsupported, including inside quotes.</summary>
    public static string Quote(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Contains('"'))
        {
            throw new ArgumentException("Batch paths and values cannot contain quotation marks.", nameof(value));
        }

        string quoted = $"\"{value}\"";
        ValidateBatchArguments(quoted);
        return quoted;
    }

    /// <summary>Validates batch paths and grammar before a caller changes its workspace or output files.</summary>
    internal static void ValidateCmdScript(string scriptPath, string scriptArguments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scriptPath);
        _ = Quote(scriptPath);
        ValidateBatchArguments(scriptArguments);
        _ = BuildAdkEnvironmentOverrides(scriptPath);
    }

    private static void ValidateBatchArguments(string arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        bool quoted = false;
        foreach (char character in arguments)
        {
            if (character == '"')
            {
                quoted = !quoted;
            }
            else if (char.IsControl(character) || "%!^&|<>".Contains(character) ||
                     (!quoted && character is '(' or ')'))
            {
                throw new ArgumentException("Batch paths and arguments contain unsupported command syntax. Use paths without expansion or control characters.", nameof(arguments));
            }
        }

        if (quoted)
        {
            throw new ArgumentException("Batch arguments contain an unmatched quotation mark.", nameof(arguments));
        }
    }

    private Task<WinPeProcessExecution> RunCmdScriptCoreAsync(
        string scriptPath,
        string scriptArguments,
        string workingDirectory,
        CancellationToken cancellationToken,
        bool callTargetScript,
        bool useCommandExtensionsStripQuoteRules,
        TimeSpan? executionTimeout)
    {
        ValidateCmdScript(scriptPath, scriptArguments);

        string normalizedScriptArguments = string.IsNullOrWhiteSpace(scriptArguments)
            ? string.Empty
            : $" {scriptArguments}";

        string scriptCommand = $"{Quote(scriptPath)}{normalizedScriptArguments}";
        string command = callTargetScript
            ? $"call {scriptCommand}"
            : scriptCommand;

        IReadOnlyDictionary<string, string>? environmentOverrides = BuildAdkEnvironmentOverrides(scriptPath);

        string switchS = useCommandExtensionsStripQuoteRules ? " /s" : string.Empty;
        string arguments = $"/d /v:off{switchS} /c \"{command}\"";
        return RunAsync(GetCommandProcessorPath(), arguments, workingDirectory, cancellationToken, environmentOverrides, executionTimeout);
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

    /// <summary>
    /// Pins ADK script inputs and executable lookup to the selected installation and native host.
    /// DandISetEnv is deliberately not invoked: it switches roots through the registry and repairs drivers.
    /// </summary>
    internal static IReadOnlyDictionary<string, string>? BuildAdkEnvironmentOverrides(
        string scriptPath, Architecture? hostArchitecture = null)
    {
        string? winPeRoot = FindWinPeRootDirectory(scriptPath);
        if (winPeRoot is null)
        {
            return null;
        }

        string architecture = (hostArchitecture ?? RuntimeInformation.OSArchitecture) switch
        {
            Architecture.X64 => "amd64",
            Architecture.Arm64 => "arm64",
            _ => throw new NotSupportedException("WinPE authoring requires an x64 or ARM64 host with native ADK tools.")
        };
        string adkRoot = Directory.GetParent(winPeRoot)?.FullName
            ?? throw new DirectoryNotFoundException("The selected WinPE script has no ADK installation root.");
        string hostTools = Path.Combine(adkRoot, "Deployment Tools", architecture);
        string oscdimgRoot = Path.Combine(hostTools, "Oscdimg");
        string dismRoot = Path.Combine(hostTools, "DISM");
        if (!Directory.Exists(oscdimgRoot) || !Directory.Exists(dismRoot) ||
            !File.Exists(Path.Combine(oscdimgRoot, "oscdimg.exe")) || !File.Exists(Path.Combine(dismRoot, "dism.exe")))
        {
            throw new DirectoryNotFoundException($"The selected ADK installation is missing native {architecture} DISM or Oscdimg tools under '{hostTools}'.");
        }
        if (hostTools.Contains(Path.PathSeparator))
        {
            throw new ArgumentException("The selected ADK path cannot contain PATH separators.", nameof(scriptPath));
        }
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["WinPERoot"] = winPeRoot,
            ["DISMRoot"] = dismRoot,
            ["OSCDImgRoot"] = oscdimgRoot,
            ["NoDefaultCurrentDirectoryInExePath"] = "1",
            ["PATH"] = string.Join(Path.PathSeparator, oscdimgRoot, dismRoot, Environment.GetEnvironmentVariable("PATH"))
        };
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
