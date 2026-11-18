// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.PreOobe;
using Foundry.PostInstall.Execution;

namespace Foundry.PostInstall.Actions;

/// <summary>
/// Installs a driver pack staged by Foundry Deploy: a Surface <c>.msi</c> in one step, or a Lenovo <c>.exe</c> that unpacks
/// to the drivers folder (substep 0) whose INF files are then installed one by one with PnPUtil (substep 1).
/// </summary>
/// <remarks>
/// PnPUtil returns actionable codes only for one INF per call. 0, 259 (no matching device, or the device already has a
/// better driver) and 3010 (reboot required) are successes; no restart is requested, because Windows restarts after the
/// specialize pass anyway. Every INF and its exit code are written to <c>Logs\PreOobe\lenovo-drivers.log</c>.
/// </remarks>
public sealed class DriverAction(string root, string windowsRoot, IPreOobeProcessExecutor processes, string? driversRoot = null)
{
    private const int NoMatchingDevice = 259;
    private const int RebootRequired = 3010;
    private static readonly TimeSpan InfTimeout = TimeSpan.FromMinutes(10);

    private readonly string drivers = driversRoot ?? Path.Combine(Path.GetPathRoot(windowsRoot)!, "Drivers");

    public async Task<ActionStepOutcome> ExecuteAsync(PreOobeExecutionAction action, int substep, CancellationToken cancellationToken)
    {
        DriverSettings settings = BuiltInSettings.Read<DriverSettings>(action);
        string package = OwnedPaths.Resolve(root, settings.PackagePath);
        string logs = OwnedPaths.Resolve(root, @"Logs\PreOobe");
        Directory.CreateDirectory(logs);
        string system = Path.Combine(windowsRoot, "System32");
        if (settings.CommandKind == "SurfaceMsi")
        {
            if (substep != 0) throw new InvalidDataException("Invalid Surface substep.");
            ProcessOutcome msi = await processes.RunAsync(new(Path.Combine(system, "msiexec.exe"),
                ["/i", package, "/qn", "/norestart", "/l*v", Path.Combine(logs, "surface-driverpack.log")],
                Path.GetDirectoryName(package)!, TimeSpan.FromMinutes(30)), cancellationToken).ConfigureAwait(false);
            return BuiltInSettings.Observe(msi);
        }

        if (settings.CommandKind != "LenovoExecutable") throw new InvalidDataException("Unsupported deferred driver command.");
        switch (substep)
        {
            case 0:
                if (!Directory.Exists(drivers)) WriteMarker("lenovo-content.owned");
                ProcessOutcome unpack = await processes.RunAsync(new(package, ["/SILENT", "/SUPPRESSMSGBOXES", "/NORESTART"],
                    Path.GetDirectoryName(package)!, TimeSpan.FromMinutes(30)), cancellationToken).ConfigureAwait(false);
                return BuiltInSettings.Observe(unpack, 1);
            case 1:
                return await InstallInfFilesAsync(Path.Combine(system, "pnputil.exe"), Path.Combine(logs, "lenovo-drivers.log"),
                    cancellationToken).ConfigureAwait(false);
            default:
                throw new InvalidDataException("Invalid Lenovo substep.");
        }
    }

    private async Task<ActionStepOutcome> InstallInfFilesAsync(string pnputil, string logPath, CancellationToken cancellationToken)
    {
        string[] infFiles = Directory.Exists(drivers)
            ? Directory.EnumerateFiles(drivers, "*.inf", SearchOption.AllDirectories).Order(StringComparer.OrdinalIgnoreCase).ToArray()
            : [];
        // Flushed per line: the log must show the INF in progress if the device loses power or the process is killed.
        using var log = new StreamWriter(logPath, append: true) { AutoFlush = true };
        if (infFiles.Length == 0)
        {
            await log.WriteLineAsync($"No INF file found under {drivers}.").ConfigureAwait(false);
            return new(false, null, "driver_package_empty");
        }

        int? firstFailure = null;
        foreach (string inf in infFiles)
        {
            // The root, not the INF folder: a deep pack folder can exceed the length allowed for a working directory.
            ProcessOutcome result = await processes.RunAsync(new(pnputil, ["/add-driver", inf, "/install"], root, InfTimeout),
                cancellationToken).ConfigureAwait(false);
            await log.WriteLineAsync($"{Path.GetRelativePath(drivers, inf)}: exit {result.ExitCode?.ToString() ?? "none"}")
                .ConfigureAwait(false);
            if (result.TerminationUncertain || result.TimedOut)
                return new(false, result.ExitCode, "execution_uncertain", TerminationUncertain: true);
            if (result.ExitCode is not (0 or NoMatchingDevice or RebootRequired)) firstFailure ??= result.ExitCode ?? -1;
        }

        return firstFailure is null ? new(true, 0) : new(false, firstFailure, "driver_install_incomplete");
    }

    private void WriteMarker(string name)
    {
        string path = OwnedPaths.Resolve(root, "State/PreOobe/" + name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        file.WriteByte(1);
        file.Flush(true);
    }
}
