// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.PreOobe;
using Foundry.PostInstall.Execution;
using Foundry.PostInstall.Windows;
namespace Foundry.PostInstall.Actions;

/// <summary>Supervises the credential-owning child; the parent retains journal and report ownership.</summary>
internal sealed class DomainJoinAction(string root, PreOobeExecutionPlan plan, string planHash, string boot,
    IPreOobeProcessExecutor processes, string executablePath)
{
    public async Task<ActionStepOutcome> ExecuteAsync(PreOobeExecutionAction action, CancellationToken token)
    {
        PreOobePlanLoader.RequireMatching(await PreOobePlanLoader.LoadAsync(root, token).ConfigureAwait(false), plan, planHash);
        var binding = DomainJoinBinding.Validate(plan); binding.RequireAction(action);
        DomainJoinBinding.RequireRunning(root, plan, planHash, boot, action.Id);
        var phases = new DomainJoinPhaseStore(root, plan, planHash);
        var initial = phases.Read();
        if (initial.Phase != DomainJoinReceiptPhase.Prepared || initial.OriginatingBootId is not null || initial.Generation != 0)
            throw new InvalidDataException("Domain work cannot be replayed.");
        var reports = new DomainJoinResultStore(root, plan, planHash);
        var report = reports.Read();
        if (report.Join.State != DomainJoinPhaseState.NotStarted || report.OriginatingBootId.Length != 0)
            throw new InvalidDataException("Domain results already exist.");
        ProcessOutcome process;
        try { process = await processes.RunAsync(new(executablePath, ["--domain-join-worker"], root, TimeSpan.FromSeconds(300)), token).ConfigureAwait(false); }
        catch (System.ComponentModel.Win32Exception) { process = new(null, string.Empty); }
        catch (Exception error) when (DomainJoinWorker.Recoverable(error)) { process = new(null, string.Empty, TerminationUncertain: true); }
        var receipt = phases.Read();
        report = reports.Reconcile(receipt, boot, process.TimedOut ? DomainJoinFailureCode.WorkerTimeout : DomainJoinFailureCode.Interrupted,
            workerUnsettled: process.TerminationUncertain);
        bool uncertain = process.TerminationUncertain;
        bool warning = uncertain || DomainJoinResultStore.HasWarnings(report);
        return new(true, FailureCode: warning ? "domain_join_warning" : null,
            RestartRequested: report.Restart == DomainJoinRestartState.Required, TerminationUncertain: uncertain, HasWarnings: warning);
    }
}
