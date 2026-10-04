// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Models.PreOobe;

/// <summary>Identifies an online join and its operation-owned sensitive payload without embedding credentials in the plan.</summary>
public sealed record DomainJoinActionParameters(string DomainName, string ComputerName, string? TargetOuDn, string CredentialPayloadPath);

/// <summary>Verifies membership on a later boot without retaining credentials.</summary>
public sealed record DomainMembershipVerificationParameters(string DomainName, string ComputerName, string JoinActionId);
