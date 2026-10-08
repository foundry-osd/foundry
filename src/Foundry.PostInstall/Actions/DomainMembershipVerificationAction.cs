// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.PreOobe;
using Foundry.Core.Models.Configuration;
using Foundry.PostInstall.Execution;
using Foundry.PostInstall.Windows;
using Serilog;
namespace Foundry.PostInstall.Actions;

/// <summary>
/// Checks only passwordless local DNS membership and the active final name on a later boot, then lets a
/// verified member skip the Windows account creation page.
/// </summary>
internal sealed class DomainMembershipVerificationAction(string root, PreOobeExecutionPlan plan, string planHash, string boot, INativeDomainJoin native)
{
    public async Task<ActionStepOutcome> ExecuteAsync(PreOobeExecutionAction action, CancellationToken token)
    {
        PreOobePlanLoader.RequireMatching(await PreOobePlanLoader.LoadAsync(root, token).ConfigureAwait(false), plan, planHash);
        var binding = DomainJoinBinding.Validate(plan); binding.RequireAction(action, verification: true);
        DomainJoinBinding.RequireRunning(root, plan, planHash, boot, action.Id);
        var store = new DomainJoinResultStore(root, plan, planHash);
        var report = store.Read();
        DomainJoinPhaseResult membership;
        bool laterBoot = report.OriginatingBootId.Length > 0 && report.OriginatingBootId != boot;
        if (report.Join.State is DomainJoinPhaseState.Failed or DomainJoinPhaseState.Skipped)
            membership = new() { State = DomainJoinPhaseState.Skipped };
        else if (!laterBoot) membership = DomainJoinWorker.Failure(DomainJoinPhaseState.Unverified, DomainJoinFailureCode.MembershipUnverified);
        else
        {
            try
            {
                token.ThrowIfCancellationRequested();
                var snapshot = native.GetMembership();
                if (snapshot.NativeErrorCode is { } error)
                    membership = DomainJoinWorker.Failure(DomainJoinPhaseState.Unverified, DomainJoinFailureCode.MembershipUnverified) with { NativeErrorCode = error };
                else if (snapshot.JoinStatus == 3 && DomainJoinCredentialContext.CanonicalizeDomainName(snapshot.DomainName) ==
                    DomainJoinCredentialContext.CanonicalizeDomainName(binding.Parameters.DomainName) &&
                    string.Equals(snapshot.ActiveComputerName, binding.Parameters.ComputerName, StringComparison.OrdinalIgnoreCase))
                    membership = new() { State = DomainJoinPhaseState.Succeeded };
                else membership = DomainJoinWorker.Failure(DomainJoinPhaseState.Failed, DomainJoinFailureCode.MembershipMismatch);
            }
            catch (Exception error) when (DomainJoinWorker.Recoverable(error))
            { membership = DomainJoinWorker.Failure(DomainJoinPhaseState.Unverified, DomainJoinFailureCode.MembershipUnverified, error); }
        }
        store.Write(report with
        {
            Membership = membership,
            Restart = laterBoot && report.Restart is DomainJoinRestartState.Required or DomainJoinRestartState.Requested ? DomainJoinRestartState.Completed : report.Restart
        });
        if (membership.State == DomainJoinPhaseState.Succeeded) SkipAccountCreationWhenRequested();
        bool warning = membership.State is not (DomainJoinPhaseState.Succeeded or DomainJoinPhaseState.Skipped);
        return new(!warning, FailureCode: warning ? "domain_membership_warning" : null, HasWarnings: warning);
    }

    /// <summary>
    /// A verified domain member signs in with domain accounts, so Windows must not ask to create a local one.
    /// It is done here, not in the answer file, so a failed or unverified join keeps the page and someone can
    /// still create an account. A refusal is logged and does not change the verified membership.
    /// </summary>
    private void SkipAccountCreationWhenRequested()
    {
        if (!File.Exists(OwnedPaths.Resolve(root, "State/PreOobe/" + DomainJoinStateFiles.SkipAccountCreationRequest))) return;
        try
        {
            native.SkipAccountCreation();
            Log.Information("Windows account creation page skipped for the verified domain member");
        }
        catch (Exception error) when (error is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            Log.Warning("Windows account creation page could not be skipped; failure type {FailureType}", error.GetType().Name);
        }
    }
}
