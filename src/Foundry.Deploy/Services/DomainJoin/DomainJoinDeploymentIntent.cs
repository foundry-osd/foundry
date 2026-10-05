// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Deploy.Services.DomainJoin;

/// <summary>Freezes the non-secret domain, computer name and OU selected before destructive confirmation.</summary>
public sealed record DomainJoinDeploymentIntent(string DomainName, string ComputerName, string? TargetOuDn);

/// <summary>Records that domain joining was requested even when an unsupported edition or simulation bypasses credentials.</summary>
public sealed record DomainJoinDeploymentRequest(DomainJoinDeploymentDisposition Disposition);

/// <summary>Distinguishes actual prepared execution, edition skip and secret-free simulation.</summary>
public enum DomainJoinDeploymentDisposition { Ready, UnsupportedEdition, DryRun }
