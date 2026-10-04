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
        catch (Exception error) when (DomainJoinWorker.Recoverable(error)) { process = new(null, string.Empty, TerminationUncertain: true); }
        DomainJoinPhaseResult join;
        DomainJoinPhaseResult placement;
        bool restart = false;
        bool uncertain = process.TerminationUncertain || process.TimedOut;
        Guid? guid = null;
        try
        {
            var receipt = phases.Read();
            if (receipt.OriginatingBootId is not null && receipt.OriginatingBootId != boot) throw new InvalidDataException("Domain receipt boot changed.");
            join = receipt.Join; placement = receipt.Placement; restart = receipt.RestartRequired; guid = receipt.ComputerObjectGuid;
            var code = process.TimedOut ? DomainJoinFailureCode.WorkerTimeout : DomainJoinFailureCode.Interrupted;
            if (receipt.Phase == DomainJoinReceiptPhase.JoinStarted) join = DomainJoinWorker.Failure(DomainJoinPhaseState.Unknown, code);
            if (receipt.Phase == DomainJoinReceiptPhase.Prepared) join = DomainJoinWorker.Failure(DomainJoinPhaseState.Failed, code);
            if (receipt.Phase == DomainJoinReceiptPhase.PlacementStarted) placement = DomainJoinWorker.Failure(DomainJoinPhaseState.Unknown, code);
            if (placement.State == DomainJoinPhaseState.NotStarted)
                placement = join.State == DomainJoinPhaseState.Succeeded ? DomainJoinWorker.Failure(DomainJoinPhaseState.Unverified, code) : new() { State = DomainJoinPhaseState.Skipped };
        }
        catch (Exception error) when (DomainJoinWorker.Recoverable(error))
        {
            join = DomainJoinWorker.Failure(DomainJoinPhaseState.Unknown, DomainJoinFailureCode.InvalidResult);
            placement = new() { State = DomainJoinPhaseState.Skipped }; uncertain = true;
        }
        reports.Write(report with
        {
            OriginatingBootId = boot,
            Join = join,
            Placement = placement,
            ComputerObjectGuid = guid,
            Restart = restart ? DomainJoinRestartState.Required : DomainJoinRestartState.NotRequired
        });
        bool warning = uncertain || join.State != DomainJoinPhaseState.Succeeded || placement.State is not (DomainJoinPhaseState.Succeeded or DomainJoinPhaseState.Skipped);
        return new(true, FailureCode: warning ? "domain_join_warning" : null, RestartRequested: restart, TerminationUncertain: uncertain, HasWarnings: warning);
    }
}
