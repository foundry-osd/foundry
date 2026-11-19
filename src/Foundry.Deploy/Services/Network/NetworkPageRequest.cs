// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Services.Runtime;

namespace Foundry.Deploy.Services.Network;

/// <summary>
/// What Foundry Deploy gives the host when it asks it to reopen the Foundry Connect page (see
/// <see cref="DeployPhaseOptions.OpenNetworkPage"/>).
/// </summary>
/// <param name="DeploymentKey">
/// A copy of the unlocked deployment key, so the reopened page needs no password, or <see langword="null"/> while Deploy is
/// locked. The host owns it and disposes it, used or not.
/// </param>
/// <param name="ThemeMode">The theme Deploy shows, a <c>Foundry.Wpf.Theme.ThemeMode</c> name.</param>
/// <param name="CultureName">The UI culture Deploy shows, for example <c>fr-FR</c>.</param>
public sealed record NetworkPageRequest(DeploymentKeyTransfer? DeploymentKey, string? ThemeMode, string? CultureName);

/// <summary>What the reopened Foundry Connect page ended with; Deploy applies it when it shows its content again.</summary>
/// <param name="ThemeMode">The theme chosen on the page, a <c>Foundry.Wpf.Theme.ThemeMode</c> name, or <see langword="null"/> to keep Deploy's.</param>
/// <param name="CultureName">The UI culture chosen on the page, or <see langword="null"/> to keep Deploy's.</param>
public sealed record NetworkPageResult(string? ThemeMode, string? CultureName);
