// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO;

namespace Foundry.Deploy.Services.Deployment;

/// <summary>Resolves the GitHub release that carries the runtimes matching the running Foundry Deploy build.</summary>
internal static class DeployReleaseTag
{
    /// <summary>
    /// Returns the first non-empty override, read in the given order, else <c>v</c> followed by the four-part Deploy version.
    /// The <c>latest</c> release is never used implicitly, so a runtime always comes from the release Deploy shipped with.
    /// </summary>
    /// <param name="deployVersion">Informational version of the running Deploy, optionally followed by <c>+</c> build metadata.</param>
    /// <param name="environment">Reads an environment variable.</param>
    /// <param name="overrideVariables">Environment variables that override the tag, highest priority first.</param>
    /// <returns>The release tag to look up.</returns>
    /// <exception cref="InvalidDataException">No override is set and the Deploy version is not a four-part version.</exception>
    internal static string Resolve(string deployVersion, Func<string, string?> environment, params string[] overrideVariables)
    {
        ArgumentNullException.ThrowIfNull(environment);
        foreach (string name in overrideVariables)
        {
            string tag = environment(name)?.Trim() ?? string.Empty;
            if (tag.Length > 0) return tag;
        }

        string version = (deployVersion ?? string.Empty).Split('+')[0];
        if (!Version.TryParse(version, out Version? parsed) || parsed.Revision < 0)
            throw new InvalidDataException("The running Deploy release cannot be identified.");
        return "v" + version;
    }
}
