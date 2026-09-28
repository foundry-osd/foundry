// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.PreOobe;
using Foundry.PostInstall.Execution;
using System.Text.Json;

namespace Foundry.PostInstall.Actions;

public sealed class AppxAction(string root, string windowsRoot, IPreOobeProcessExecutor processes)
{
    public async Task<ActionStepOutcome> ExecuteAsync(PreOobeExecutionAction action, int substep, CancellationToken cancellationToken)
    {
        AppxSettings settings = BuiltInSettings.Read<AppxSettings>(action);
        string executable = Path.Combine(windowsRoot, "System32", "dism.exe");
        string log = OwnedPaths.Resolve(root, @"Logs\PreOobe\appx-servicing.log");
        Directory.CreateDirectory(Path.GetDirectoryName(log)!);
        string[] common = ["/Online", "/English", "/NoRestart", "/LogPath:" + log];
        string snapshot = OwnedPaths.Resolve(root, "State/PreOobe/appx-" + action.Id + ".json");
        if (substep == 0)
        {
            ProcessOutcome inventory = await processes.RunAsync(new(executable,
            [.. common, "/Get-ProvisionedAppxPackages"], root, TimeSpan.FromMinutes(5)), cancellationToken).ConfigureAwait(false);
            if (inventory.ExitCode != 0 || inventory.TerminationUncertain) return BuiltInSettings.Observe(inventory);
            if (inventory.OutputTruncated) return new(false, FailureCode: "appx_inventory_truncated");
            IReadOnlyList<ProvisionedPackage> packages = DismInventory.Select(DismInventory.Parse(inventory.StandardOutput),
                settings.PackageNames, action.BuiltInKind == PreOobeBuiltInKind.AiRemoval);
            Directory.CreateDirectory(Path.GetDirectoryName(snapshot)!);
            using var file = new FileStream(snapshot, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            file.Write(JsonSerializer.SerializeToUtf8Bytes(packages));
            file.Flush(true);
            return new(true, NextSubstep: 1);
        }
        if (new FileInfo(snapshot).Length > 4 * 1024 * 1024) throw new InvalidDataException("AppX state is too large.");
        var selected = JsonSerializer.Deserialize<ProvisionedPackage[]>(await File.ReadAllBytesAsync(snapshot, cancellationToken).ConfigureAwait(false))
            ?? throw new InvalidDataException("AppX state is missing.");
        if (substep > selected.Length) return new(true);
        ProvisionedPackage package = selected[substep - 1];
        ProcessOutcome result = await processes.RunAsync(new(executable,
            [.. common, "/Remove-ProvisionedAppxPackage", "/PackageName:" + package.PackageName],
            root, TimeSpan.FromMinutes(10)), cancellationToken).ConfigureAwait(false);
        if (result.TerminationUncertain || result.ExitCode == 1641) return BuiltInSettings.Observe(result);
        return new(true, RestartRequested: result.ExitCode == 3010, NextSubstep: substep + 1,
            HasWarnings: result.ExitCode is not (0 or 3010));
    }
}
