// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Connect.Network;
using Foundry.Core.Services.Runtime;

namespace Foundry.Connect;

/// <summary>
/// What the Foundry.WinPE UI host hands to a Foundry Connect page reopened from the deployment's Network menu, while the
/// Foundry Deploy phase stays alive underneath.
/// </summary>
/// <param name="PreviousProvisioning">
/// The provisioning outcome of the first Connect page (<see cref="ConnectPhaseResult.Provisioning"/>), replayed instead of
/// provisioning again: the provisioning reconnects every linked Ethernet interface, which would drop the operator's link.
/// <see langword="null"/> when the first Connect page did not run; the reopened page then provisions once.
/// </param>
/// <param name="DeploymentKey">
/// A copy of the deployment key the deployment holds, or <see langword="null"/>. The caller owns it; Connect takes its bytes,
/// unlocks the network secrets with them when the media's key check accepts them, and leaves the copy zeroed either way. A
/// missing or refused key leaves the password prompt on demand, as on the first page.
/// </param>
public sealed record ConnectRevisit(ProvisioningOutcome? PreviousProvisioning, DeploymentKeyTransfer? DeploymentKey);
