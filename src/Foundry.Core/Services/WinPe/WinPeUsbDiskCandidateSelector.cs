// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Services.WinPe;

/// <summary>
/// Restores a USB disk selection after the candidate list is refreshed, because disk numbers can change
/// when media is repartitioned or re-enumerated.
/// </summary>
public static class WinPeUsbDiskCandidateSelector
{
    /// <summary>
    /// Finds the refreshed candidate for <paramref name="previous"/> by unique id, then by serial number,
    /// and by disk number only when the previous selection has neither identifier.
    /// An identifier that matches several candidates is ambiguous and is not used.
    /// When no previous selection exists, or it cannot be found, a candidate is chosen only when it is the sole one,
    /// so a different disk is never selected silently.
    /// </summary>
    public static WinPeUsbDiskCandidate? Reselect(
        IReadOnlyList<WinPeUsbDiskCandidate> candidates,
        WinPeUsbDiskCandidate? previous)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        WinPeUsbDiskCandidate? match = previous is null ? null : FindByStableIdentity(candidates, previous);
        if (match is not null)
        {
            return match;
        }

        return previous is null ? candidates.FirstOrDefault() : candidates.Count == 1 ? candidates[0] : null;
    }

    private static WinPeUsbDiskCandidate? FindByStableIdentity(
        IReadOnlyList<WinPeUsbDiskCandidate> candidates,
        WinPeUsbDiskCandidate previous)
    {
        bool hasUniqueId = !string.IsNullOrWhiteSpace(previous.UniqueId);
        bool hasSerialNumber = !string.IsNullOrWhiteSpace(previous.SerialNumber);
        if (!hasUniqueId && !hasSerialNumber)
        {
            return FindSingle(candidates, candidate => candidate.DiskNumber == previous.DiskNumber);
        }

        WinPeUsbDiskCandidate? match = hasUniqueId
            ? FindSingle(candidates, candidate => EqualIdentifier(candidate.UniqueId, previous.UniqueId))
            : null;
        return match ?? (hasSerialNumber
            ? FindSingle(candidates, candidate => EqualIdentifier(candidate.SerialNumber, previous.SerialNumber))
            : null);
    }

    private static WinPeUsbDiskCandidate? FindSingle(
        IReadOnlyList<WinPeUsbDiskCandidate> candidates,
        Func<WinPeUsbDiskCandidate, bool> predicate)
    {
        WinPeUsbDiskCandidate[] matches = candidates.Where(predicate).Take(2).ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    private static bool EqualIdentifier(string actual, string expected)
    {
        return string.Equals(actual.Trim(), expected.Trim(), StringComparison.OrdinalIgnoreCase);
    }
}
