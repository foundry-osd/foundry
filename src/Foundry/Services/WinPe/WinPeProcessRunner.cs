using System.Diagnostics;
using System.Text;

namespace Foundry.Services.WinPe;

internal sealed record WinPeProcessExecution
{
    public int ExitCode { get; init; }
    public string FileName { get; init; } = string.Empty;
    public string Arguments { get; init; } = string.Empty;
    public string WorkingDirectory { get; init; } = string.Empty;
    public string StandardOutput { get; init; } = string.Empty;
    public string StandardError { get; init; } = string.Empty;

    public bool IsSuccess => ExitCode == 0;

    public string ToDiagnosticText()
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Command: {FileName} {Arguments}".TrimEnd());
        builder.AppendLine($"WorkingDirectory: {WorkingDirectory}");
        builder.AppendLine($"ExitCode: {ExitCode}");

        if (!string.IsNullOrWhiteSpace(StandardOutput))
        {
            builder.AppendLine("StdOut:");
            builder.AppendLine(StandardOutput.Trim());
        }

        if (!string.IsNullOrWhiteSpace(StandardError))
        {
            builder.AppendLine("StdErr:");
            builder.AppendLine(StandardError.Trim());
        }

        return builder.ToString().Trim();
    }
}

internal sealed class WinPeProcessRunner
{
    private const string InternalSetEnvKey = "FOUNDRY_ADK_SETENV_PATH";

    public async Task<WinPeProcessExecution> RunAsync(
        string fileName,
        string arguments,
        string workingDirectory,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string>? environmentOverrides = null)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new ArgumentException("Executable path is required.", nameof(fileName));
        }

        if (string.IsNullOrWhiteSpace(workingDirectory))
        {
            throw new ArgumentException("Working directory is required.", nameof(workingDirectory));
        }

        Directory.CreateDirectory(workingDirectory);

        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        if (environmentOverrides is not null)
        {
            foreach ((string key, string value) in environmentOverrides)
            {
                if (key.StartsWith("FOUNDRY_", StringComparison.Ordinal))
                {
                    continue;
                }

                startInfo.Environment[key] = value;
            }
        }

        using var process = new Process
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true
        };

        var stdoutBuilder = new StringBuilder();
        var stderrBuilder = new StringBuilder();

        process.OutputDataReceived += (_, args) =>
        {
            if (args.Data is not null)
            {
                stdoutBuilder.AppendLine(args.Data);
            }
        };

        process.ErrorDataReceived += (_, args) =>
        {
            if (args.Data is not null)
            {
                stderrBuilder.AppendLine(args.Data);
            }
        };

        if (!process.Start())
        {
            throw new InvalidOperationException($"Failed to start process '{fileName}'.");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var registration = cancellationToken.Register(() =>
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
                // Ignore failures while attempting to terminate a canceled process.
            }
        });

        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        return new WinPeProcessExecution
        {
            ExitCode = process.ExitCode,
            FileName = fileName,
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            StandardOutput = stdoutBuilder.ToString(),
            StandardError = stderrBuilder.ToString()
        };
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

    private Task<WinPeProcessExecution> RunCmdScriptCoreAsync(
        string scriptPath,
        string scriptArguments,
        string workingDirectory,
        CancellationToken cancellationToken,
        bool callTargetScript,
        bool useCommandExtensionsStripQuoteRules)
    {
        if (string.IsNullOrWhiteSpace(scriptPath))
        {
            throw new ArgumentException("Script path is required.", nameof(scriptPath));
        }

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
        return RunAsync(GetCommandProcessorPath(), arguments, workingDirectory, cancellationToken, environmentOverrides);
    }

    public static string Quote(string value)
    {
        return value.Contains(' ', StringComparison.Ordinal)
            ? $"\"{value}\""
            : value;
    }

    private static string GetCommandProcessorPath()
    {
        var cmdPath = Environment.GetEnvironmentVariable("ComSpec");
        if (!string.IsNullOrWhiteSpace(cmdPath))
        {
            return cmdPath;
        }

        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "cmd.exe");
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

        // Internal helper key used to prepend ADK environment initialization in the same cmd.exe session.
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
