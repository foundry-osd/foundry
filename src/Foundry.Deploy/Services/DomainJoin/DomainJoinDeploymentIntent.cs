// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;

namespace Foundry.Deploy.Services.DomainJoin;

/// <summary>Freezes the non-secret destination and name selected before destructive confirmation.</summary>
public sealed record DomainJoinDeploymentIntent(string DomainName, string ComputerName, string? TargetOuDn);

/// <summary>Preserves requested mode even when unsupported editions or simulation bypass credentials.</summary>
public sealed record DomainJoinDeploymentRequest(DomainJoinMode Mode, DomainJoinDeploymentDisposition Disposition,
    DomainJoinPreparationFailure? FailureCode = null);

/// <summary>Distinguishes actual prepared execution, edition skip and secret-free simulation.</summary>
public enum DomainJoinDeploymentDisposition { Ready, UnsupportedEdition, DryRun }
