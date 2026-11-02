// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;

namespace Foundry.Core.Services.Runtime;

/// <summary>
/// Compares boot media with the latest published Foundry release after a runtime has started.
/// </summary>
/// <remarks>
/// Boot media run the release that created them, so the running runtime never reveals a newer release by itself. A check
/// exists only when it can change the advice: an eligible, published runtime whose boot media carry a known authoring
/// release and no recommendation yet.
/// </remarks>
/// <param name="AuthoringVersion">Foundry release that created the boot media.</param>
/// <param name="RuntimeVersion">Release of the running runtime.</param>
public sealed record BootMediaLatestReleaseCheck(string AuthoringVersion, string RuntimeVersion)
{
    /// <summary>
    /// Returns a check when the startup advice is <see cref="BootMediaUpdateReason.None"/> for an eligible runtime with a
    /// published release version, else <see langword="null"/>.
    /// </summary>
    /// <param name="authoringVersion">Foundry release that created the boot media.</param>
    /// <param name="runtimeVersion">Release of the running runtime; an unpublished build gets no check.</param>
    /// <param name="isEligibleRuntime">Whether the runtime environment can receive advice at all.</param>
    public static BootMediaLatestReleaseCheck? Create(string? authoringVersion, string? runtimeVersion, bool isEligibleRuntime) =>
        isEligibleRuntime &&
        BootMediaFreshnessPolicy.TryParseReleaseVersion(runtimeVersion, out _) &&
        BootMediaFreshnessPolicy.Evaluate(authoringVersion, runtimeVersion, isEligibleRuntime) == BootMediaUpdateReason.None
            ? new BootMediaLatestReleaseCheck(authoringVersion!, runtimeVersion!)
            : null;

    /// <summary>Reads the latest release and evaluates the boot media against it.</summary>
    /// <param name="readLatestVersion">Reads the latest published release; it yields <see langword="null"/> when unknown.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns><see cref="BootMediaUpdateReason.NewerRelease"/> when the latest release is newer, else <see cref="BootMediaUpdateReason.None"/>.</returns>
    public async Task<BootMediaUpdateReason> EvaluateAsync(LatestReleaseVersionReader readLatestVersion, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(readLatestVersion);
        string? latestReleaseVersion = await readLatestVersion(cancellationToken).ConfigureAwait(false);
        return BootMediaFreshnessPolicy.Evaluate(AuthoringVersion, RuntimeVersion, isEligibleRuntime: true, latestReleaseVersion);
    }
}
