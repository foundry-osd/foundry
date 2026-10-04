// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.PreOobe;
using Foundry.PostInstall.Execution;
using Foundry.PostInstall.Windows;

namespace Foundry.PostInstall.Actions;

public sealed class PreOobeActionExecutor(string root, string windowsRoot, PreOobeExecutionPlan plan,
    IPreOobeProcessExecutor processes, ICertificateImporter certificates, string executablePath, string? planHash = null, Func<string>? bootIdentity = null) : IPreOobeActionExecutor
{
    public async Task<ActionStepOutcome> ExecuteAsync(PreOobeExecutionAction action, int substep, CancellationToken cancellationToken)
    {
        if (action.CustomAction is not null)
            return await new CustomAction(root, windowsRoot, plan, processes).ExecuteAsync(action, cancellationToken).ConfigureAwait(false);
        return action.BuiltInKind switch
        {
            PreOobeBuiltInKind.Driver => await new DriverAction(root, windowsRoot, processes).ExecuteAsync(action, substep, cancellationToken).ConfigureAwait(false),
            PreOobeBuiltInKind.Network => await new NetworkAction(root, windowsRoot, processes, certificates).ExecuteAsync(action, cancellationToken).ConfigureAwait(false),
            PreOobeBuiltInKind.Appx or PreOobeBuiltInKind.AiRemoval => await new AppxAction(root, windowsRoot, processes).ExecuteAsync(action, substep, cancellationToken).ConfigureAwait(false),
            PreOobeBuiltInKind.Activation => await ActivateAsync(cancellationToken).ConfigureAwait(false),
            PreOobeBuiltInKind.DomainJoinAndPlacement => await new DomainJoinAction(root, plan, RequireDomainHash(),
                (bootIdentity ?? BootIdentityProvider.Read)(), processes, executablePath).ExecuteAsync(action, cancellationToken).ConfigureAwait(false),
            PreOobeBuiltInKind.VerifyDomainMembership => await new DomainMembershipVerificationAction(root, plan, RequireDomainHash(),
                (bootIdentity ?? BootIdentityProvider.Read)(), new NativeDomainJoin()).ExecuteAsync(action, cancellationToken).ConfigureAwait(false),
            PreOobeBuiltInKind.Cleanup => await CleanupAsync(cancellationToken).ConfigureAwait(false),
            _ => throw new InvalidDataException("Unsupported built-in action.")
        };
    }

    private string RequireDomainHash() => planHash ?? throw new InvalidDataException("Domain execution requires its fixed plan hash.");

    private async Task<ActionStepOutcome> ActivateAsync(CancellationToken cancellationToken)
    {
        ProcessOutcome result = await processes.RunAsync(new(executablePath, ["--activation-worker"], root,
            TimeSpan.FromSeconds(60)), cancellationToken).ConfigureAwait(false);
        return new(result.ExitCode == 0 && !result.TerminationUncertain, result.ExitCode,
            result.ExitCode == 0 ? null : "activation_failed", TerminationUncertain: result.TerminationUncertain || result.TimedOut);
    }

    private async Task<ActionStepOutcome> CleanupAsync(CancellationToken cancellationToken)
    {
        bool warning = false;
        try
        {
            ActionStepOutcome registry = await new DriverAction(root, windowsRoot, processes).CleanupRegistryAsync(cancellationToken).ConfigureAwait(false);
            if (registry.TerminationUncertain) return registry;
            warning = registry.HasWarnings;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception) { warning = true; }
        string marker = OwnedPaths.Resolve(root, "State/PreOobe/lenovo-content.owned");
        if (File.Exists(marker))
        {
            string drivers = Path.Combine(Path.GetPathRoot(windowsRoot)!, "Drivers");
            try { OwnedPaths.DeleteDirectory(drivers); File.Delete(marker); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { warning = true; }
        }
        return new(true, HasWarnings: warning);
    }
}
