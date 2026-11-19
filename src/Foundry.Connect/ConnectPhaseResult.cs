// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Connect.Network;
using Foundry.Core.Models.Runtime;
using Foundry.Core.Services.Runtime;

namespace Foundry.Connect;

/// <summary>Outcome of <see cref="ConnectPhase.RunAsync"/>.</summary>
/// <param name="ExitCode">
/// A <see cref="ChildRuntimeExitCode"/> value: <see cref="ChildRuntimeExitCode.Success"/> when the network is ready, otherwise
/// the user abort or the startup, configuration or runtime failure that ended Connect.
/// </param>
/// <param name="DeploymentKey">
/// The deployment key unlocked in Connect, set only on success when the media carries a key check. The caller owns it and
/// disposes it, whether the Deploy phase takes it or not.
/// </param>
/// <param name="ThemeMode">The theme Connect ended with, a <c>Foundry.Wpf.Theme.ThemeMode</c> name such as <c>Dark</c>.</param>
/// <param name="CultureName">The UI culture Connect ended with, for example <c>fr-FR</c>.</param>
/// <remarks>
/// On success Connect's content stays shown in the shell, disabled, or its status page when the network gate skipped the
/// Connect page, until the next phase's content replaces it. A Connect page reopened from the deployment
/// (<see cref="ConnectPhaseOptions.Revisit"/>) never carries a deployment key: a key it obtained is zeroed.
/// </remarks>
public sealed record ConnectPhaseResult(
    int ExitCode,
    DeploymentKeyTransfer? DeploymentKey,
    string ThemeMode,
    string CultureName)
{
    /// <summary>
    /// Gets the provisioning outcome of the session, to hand to a Connect page reopened later
    /// (<see cref="ConnectRevisit.PreviousProvisioning"/>), or <see langword="null"/> when Connect ended before it provisioned.
    /// Its Wi-Fi profile document is released (its XML cleared) once Connect ends.
    /// </summary>
    public ProvisioningOutcome? Provisioning { get; init; }
}
