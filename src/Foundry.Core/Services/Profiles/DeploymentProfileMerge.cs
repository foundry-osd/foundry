// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Profiles;

namespace Foundry.Core.Services.Profiles;

/// <summary>Preserves explicitly omitted local values only when their authenticated logical context still matches.</summary>
public static class DeploymentProfileMerge
{
    /// <summary>Returns independent owned buffers. Deletion, unavailable values, and changed contexts never fall back.</summary>
    public static DeploymentProfileDocument PreserveOmittedLocalValues(DeploymentProfileDocument incoming, DeploymentProfileDocument local)
    {
        if (incoming.ProfileId != local.ProfileId)
            throw new ArgumentException("Local values belong to a different profile.", nameof(local));
        return incoming with
        {
            Secrets = new DeploymentProfileSecrets
            {
                Entries = incoming.Secrets.Entries.Select(secret =>
                {
                    DeploymentProfileSecret selected = secret;
                    if (secret.State == ProfileValueState.Omitted)
                    {
                        selected = local.Secrets.Entries.SingleOrDefault(candidate => candidate.Purpose == secret.Purpose &&
                            candidate.Identity == secret.Identity && candidate.State is ProfileValueState.Present or ProfileValueState.Blank) ?? secret;
                    }
                    return selected with { Value = selected.Value?.ToArray() };
                }).ToArray()
            },
            Assets = incoming.Assets.Select(asset =>
            {
                DeploymentProfileAsset selected = asset;
                if (asset.State == ProfileValueState.Omitted && asset.Sha256 is not null)
                {
                    selected = local.Assets.SingleOrDefault(candidate => candidate.Id == asset.Id && candidate.Kind == asset.Kind &&
                        candidate.State == ProfileValueState.Present && string.Equals(candidate.Sha256, asset.Sha256, StringComparison.OrdinalIgnoreCase)) ?? asset;
                }
                return selected with { Content = selected.Content?.ToArray() };
            }).ToArray()
        };
    }
}
