// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Diagnostics;
using Serilog;

namespace Foundry.Bootstrap.Processes;

/// <summary>Observes application lifetimes without owning their termination or buffering their output.</summary>
internal sealed class ApplicationLauncher(ILogger logger) : IApplicationLauncher
{
    public Task<int> RunConnectAsync(string executable, string configurationPath,
        IReadOnlyDictionary<string, string?> environment, CancellationToken cancellationToken)
    {
        string[] arguments = File.Exists(configurationPath) ? ["--config", configurationPath] : [];
        return RunAsync(CreateStartInfo(executable, environment, arguments), cancellationToken);
    }

    public Task<int> StartDeployAsync(string executable, IReadOnlyDictionary<string, string?> environment,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using Process process = Start(CreateStartInfo(executable, environment, []));
        return Task.FromResult(process.Id);
    }

    internal async Task<int> RunAsync(ProcessStartInfo startInfo, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using Process process = Start(startInfo);
        return await ObserveAsync(process, cancellationToken).ConfigureAwait(false);
    }

    internal async Task<int> ObserveAsync(Process process, CancellationToken cancellationToken)
    {
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        logger.Information("Application process {ProcessId} exited with code {ExitCode}", process.Id, process.ExitCode);
        return process.ExitCode;
    }

    internal static ProcessStartInfo CreateStartInfo(string executable,
        IReadOnlyDictionary<string, string?> environment, IEnumerable<string> arguments)
    {
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(executable))!
        };
        foreach (string argument in arguments) { start.ArgumentList.Add(argument); }
        foreach ((string key, string? value) in environment)
        {
            if (value is null) { start.Environment.Remove(key); }
            else { start.Environment[key] = value; }
        }

        return start;
    }

    private Process Start(ProcessStartInfo start)
    {
        Process process = Process.Start(start) ?? throw new InvalidOperationException("The application process could not be created.");
        logger.Information("Application process {ProcessId} launched", process.Id);
        return process;
    }
}
