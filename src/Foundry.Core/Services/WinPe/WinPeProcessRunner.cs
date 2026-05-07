using System.Diagnostics;
using System.Text;

namespace Foundry.Core.Services.WinPe;

public sealed class WinPeProcessRunner : IWinPeProcessOutputRunner
{
    private const string InternalSetEnvKey = "FOUNDRY_ADK_SETENV_PATH";

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

    public async Task<WinPeProcessExecution> RunWithOutputAsync(
        string fileName,
        string arguments,
        string workingDirectory,
        Action<string>? onOutputData,
        Action<string>? onErrorData,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string>? environmentOverrides = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);

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
                onOutputData?.Invoke(args.Data);
                stdoutBuilder.AppendLine(args.Data);
            }
        };

        process.ErrorDataReceived += (_, args) =>
        {
            if (args.Data is not null)
            {
                onErrorData?.Invoke(args.Data);
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
                // Best effort during cancellation.
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
        return RunAsync(GetCommandProcessorPath(), arguments, workingDirectory, cancellationToken, environmentOverrides);
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
