// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Services.Runtime;

/// <summary>
/// The GitHub release a Foundry program downloads its runtimes from: a release tag, or the latest release when
/// <see cref="Tag"/> is <see langword="null"/>.
/// </summary>
/// <param name="Tag">The release tag, or <see langword="null"/> for the latest release of an unpublished build.</param>
public readonly record struct FoundryReleaseSelection(string? Tag)
{
    private const string ReleasesApiUrl = "https://api.github.com/repos/foundry-osd/foundry/releases/";

    /// <summary>Gets whether the selection is the latest release, which only unpublished builds use.</summary>
    public bool IsLatest => Tag is null;

    /// <summary>Gets the GitHub API URL of the selected release.</summary>
    public string ApiUrl => IsLatest ? ReleasesApiUrl + "latest" : ReleasesApiUrl + "tags/" + Uri.EscapeDataString(Tag!);
}

/// <summary>
/// Resolves the release a Foundry program downloads runtimes from, so boot media, Bootstrap repairs and Deploy downloads
/// always use the release of the running program.
/// </summary>
/// <remarks>
/// A published build never resolves to the latest release: runtimes of another release could mix with the boot media
/// or the configuration it was created with. Only the unpublished <see cref="UnpublishedVersion"/> of local builds falls
/// back to the latest release, and callers log that fallback as a warning.
/// </remarks>
public static class FoundryReleaseTag
{
    /// <summary>The version of local builds, which matches no published release.</summary>
    public const string UnpublishedVersion = "1.0.0.0";

    /// <summary>The environment variable that overrides the release for every Foundry program.</summary>
    public const string ReleaseTagVariable = "FOUNDRY_RELEASE_TAG";

    /// <summary>
    /// Returns the first non-empty override, read in the given order, else <c>v</c> followed by the four-part version,
    /// else the latest release for <see cref="UnpublishedVersion"/>.
    /// </summary>
    /// <param name="version">Informational version of the running program, optionally followed by <c>+</c> build metadata.</param>
    /// <param name="environment">Reads an environment variable.</param>
    /// <param name="overrideVariables">Environment variables that override the release, highest priority first.</param>
    /// <returns>The release to download from.</returns>
    /// <exception cref="InvalidDataException">No override is set and the version is not a four-part version.</exception>
    public static FoundryReleaseSelection Resolve(string? version, Func<string, string?> environment, params string[] overrideVariables)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(overrideVariables);
        foreach (string name in overrideVariables)
        {
            string tag = environment(name)?.Trim() ?? string.Empty;
            if (tag.Length > 0) return new(tag);
        }

        string releaseVersion = (version ?? string.Empty).Split('+')[0].Trim();
        if (!Version.TryParse(releaseVersion, out Version? parsed) || parsed.Revision < 0)
            throw new InvalidDataException("The running Foundry release cannot be identified.");
        return releaseVersion == UnpublishedVersion ? new(null) : new("v" + releaseVersion);
    }
}
