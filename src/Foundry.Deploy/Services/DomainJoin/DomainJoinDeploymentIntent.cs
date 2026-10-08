// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Deploy.Services.DomainJoin;

/// <summary>Freezes the non-secret domain, computer name and OU selected before destructive confirmation.</summary>
public sealed record DomainJoinDeploymentIntent(string DomainName, string ComputerName, string? TargetOuDn);

/// <summary>
/// Records that domain joining was requested even when an unsupported edition or simulation bypasses credentials,
/// with the mode and the origin of the chosen domain and OU for usage telemetry.
/// </summary>
public sealed record DomainJoinDeploymentRequest(DomainJoinDeploymentDisposition Disposition,
    Foundry.Core.Models.Configuration.DomainJoinMode Mode = Foundry.Core.Models.Configuration.DomainJoinMode.Interactive,
    DomainJoinOuSource OuSource = DomainJoinOuSource.None,
    DomainJoinDomainSource DomainSource = DomainJoinDomainSource.None);

/// <summary>Tells where the joined domain came from, without naming it.</summary>
public enum DomainJoinDomainSource { None, Default, Selected, Typed }

/// <summary>Tells where the join target OU came from, without naming it.</summary>
public enum DomainJoinOuSource { None, Default, Selected, Typed }

/// <summary>Distinguishes actual prepared execution, edition skip and secret-free simulation.</summary>
public enum DomainJoinDeploymentDisposition { Ready, UnsupportedEdition, DryRun }
