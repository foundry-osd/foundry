// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.PreOobe;
using Foundry.PostInstall.Execution;

namespace Foundry.PostInstall.Actions;

public sealed class DriverAction(string root, string windowsRoot, IPreOobeProcessExecutor processes)
{
    private const string DriverRegistryPath = @"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\UnattendSettings\PnPUnattend\DriverPaths\1";

    public async Task<ActionStepOutcome> ExecuteAsync(PreOobeExecutionAction action, int substep, CancellationToken cancellationToken)
    {
        DriverSettings settings = BuiltInSettings.Read<DriverSettings>(action);
        string package = OwnedPaths.Resolve(root, settings.PackagePath);
        string logs = OwnedPaths.Resolve(root, @"Logs\PreOobe");
        Directory.CreateDirectory(logs);
        string system = Path.Combine(windowsRoot, "System32");
        ProcessCommand command;
        int? next;
        if (settings.CommandKind == "SurfaceMsi")
        {
            if (substep != 0) throw new InvalidDataException("Invalid Surface substep.");
            command = new(Path.Combine(system, "msiexec.exe"),
                ["/i", package, "/qn", "/norestart", "/l*v", Path.Combine(logs, "surface-driverpack.log")],
                Path.GetDirectoryName(package)!, TimeSpan.FromMinutes(30));
            next = null;
        }
        else if (settings.CommandKind == "LenovoExecutable")
        {
            if (substep == 0 && !Directory.Exists(Path.Combine(Path.GetPathRoot(windowsRoot)!, "Drivers")))
                WriteMarker("lenovo-content.owned");
            if (substep == 1) WriteMarker("lenovo-registry.owned");
            (command, next) = substep switch
            {
                0 => (new ProcessCommand(package, ["/SILENT", "/SUPPRESSMSGBOXES", "/NORESTART"], Path.GetDirectoryName(package)!, TimeSpan.FromMinutes(30)), 1),
                1 => (new ProcessCommand(Path.Combine(system, "reg.exe"), ["add", DriverRegistryPath, "/v", "Path", "/t", "REG_SZ", "/d", @"C:\Drivers", "/f"], root, TimeSpan.FromMinutes(1)), 2),
                2 => (new ProcessCommand(Path.Combine(system, "pnpunattend.exe"), ["AuditSystem", "/L"], root, TimeSpan.FromMinutes(30)), 3),
                3 => (new ProcessCommand(Path.Combine(system, "reg.exe"), ["delete", DriverRegistryPath, "/v", "Path", "/f"], root, TimeSpan.FromMinutes(1)), (int?)null),
                _ => throw new InvalidDataException("Invalid Lenovo substep.")
            };
        }
        else throw new InvalidDataException("Unsupported deferred driver command.");
        ProcessOutcome result = await processes.RunAsync(command, cancellationToken).ConfigureAwait(false);
        if (settings.CommandKind == "LenovoExecutable" && substep == 3 && !result.TerminationUncertain && result.ExitCode != 1641)
        {
            if (result.ExitCode == 0) File.Delete(Marker("lenovo-registry.owned"));
            return new(true, result.ExitCode, HasWarnings: result.ExitCode != 0);
        }
        return BuiltInSettings.Observe(result, next);
    }

    public async Task<ActionStepOutcome> CleanupRegistryAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(Marker("lenovo-registry.owned"))) return new(true);
        ProcessOutcome result = await processes.RunAsync(new ProcessCommand(Path.Combine(windowsRoot, "System32", "reg.exe"),
            ["delete", DriverRegistryPath, "/v", "Path", "/f"], root, TimeSpan.FromSeconds(30)), cancellationToken).ConfigureAwait(false);
        if (result.ExitCode == 0 && !result.TerminationUncertain) File.Delete(Marker("lenovo-registry.owned"));
        if (result.TerminationUncertain || result.TimedOut || result.ExitCode is null or 1641)
            return new(false, result.ExitCode, "cleanup_uncertain", TerminationUncertain: true);
        return new(true, result.ExitCode, HasWarnings: result.ExitCode != 0);
    }

    private string Marker(string name) => OwnedPaths.Resolve(root, "State/PreOobe/" + name);

    private void WriteMarker(string name)
    {
        string path = Marker(name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        file.WriteByte(1);
        file.Flush(true);
    }
}
