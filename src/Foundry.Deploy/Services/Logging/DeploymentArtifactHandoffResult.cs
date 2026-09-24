// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Deploy.Services.Logging;

/// <summary>Records verified diagnostic publication without replacing the deployment outcome.</summary>
public sealed record DeploymentArtifactHandoffResult(
    string EffectiveRootPath,
    IReadOnlyList<string> PersistedArtifacts,
    IReadOnlyList<string> Failures,
    bool CanRetireSource);
