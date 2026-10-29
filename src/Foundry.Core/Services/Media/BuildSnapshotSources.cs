// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Text;
using Foundry.Core.Services.Configuration;
using Foundry.Core.Services.Files;
using Foundry.Core.Services.NetworkCache;

namespace Foundry.Core.Services.Media;

/// <summary>
/// What a media build captures beside the configuration: the library holding the referenced authoring files and the
/// session passwords that never enter the document.
/// </summary>
/// <param name="Files">The authoring file library the referenced files are copied from, with hash verification.</param>
public sealed record BuildSnapshotSources(AuthoringFileLibrary Files)
{
    /// <summary>Gets the network passwords for the captured network settings.</summary>
    public NetworkSecrets Network { get; init; } = new();

    /// <summary>Gets the password of the Autopilot boot media PFX.</summary>
    public string? AutopilotPfxPassword { get; init; }

    /// <summary>Gets the network cache account passwords, keyed by account id.</summary>
    public IReadOnlyDictionary<string, string> NetworkCachePasswords { get; init; } = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>Gets the signed content index binding, required when the network cache is active.</summary>
    public NetworkCacheIndexBinding? NetworkCacheIndex { get; init; }

    // Records print their members; a password is reported as present or absent, never printed.
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("Network = ").Append(Network).Append(", HasAutopilotPfxPassword = ").Append(AutopilotPfxPassword is not null)
            .Append(", NetworkCachePasswordCount = ").Append(NetworkCachePasswords.Count)
            .Append(", HasNetworkCacheIndex = ").Append(NetworkCacheIndex is not null);
        return true;
    }
}
