// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Deploy.Services.Deployment;

public sealed record WindowsOptionalFeatureServicingResult
{
    public int RequestedActionCount { get; init; }
    public int ChangedActionCount { get; init; }
    public int AlreadySatisfiedActionCount { get; init; }
    public IReadOnlyList<string> UnavailableEnableActionIds { get; init; } = [];
    public bool MatchingSourceUsed { get; init; }
}
