// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Models.PreOobe;

/// <summary>Identifies an online join and its operation-owned sensitive payload without embedding credentials in the plan.</summary>
public sealed record DomainJoinActionParameters(string DomainName, string ComputerName, string? TargetOuDn, string CredentialPayloadPath);

/// <summary>Names the owned state files Deploy stages for the join besides the plan and its seeds.</summary>
public static class DomainJoinStateFiles
{
    /// <summary>
    /// Empty marker asking the runtime to skip the Windows account creation page once membership is verified.
    /// Deploy stages it only with the answer file Foundry generates; an imported file keeps its own OOBE choices.
    /// </summary>
    public const string SkipAccountCreationRequest = "skip-account-creation.request";
}

/// <summary>Verifies membership on a later boot without retaining credentials.</summary>
public sealed record DomainMembershipVerificationParameters(string DomainName, string ComputerName, string JoinActionId);
