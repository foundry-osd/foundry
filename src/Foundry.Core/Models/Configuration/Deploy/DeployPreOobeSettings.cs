// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Models.Configuration.Deploy;

/// <summary>Carries custom actions and binds their packages to an external media generation. Deploy consults the binding only when an enabled action uses a package; media publication supplies the digest.</summary>
public sealed record DeployPreOobeSettings
{
    public bool IsEnabled { get; init; }
    public IReadOnlyList<PreOobeActionSettings> Actions { get; init; } = [];
    public string? ManifestId { get; init; }
    public string? ManifestHash { get; init; }
}
