// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

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
/// On success Connect's content stays shown in the shell, disabled, until the next phase's content replaces it.
/// </remarks>
public sealed record ConnectPhaseResult(
    int ExitCode,
    DeploymentKeyTransfer? DeploymentKey,
    string ThemeMode,
    string CultureName);
