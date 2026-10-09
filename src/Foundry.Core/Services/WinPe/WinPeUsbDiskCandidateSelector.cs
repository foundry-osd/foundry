// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Utilities.Storage;

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
    /// Without a previous selection the first candidate is selected. When the previous selection cannot be found,
    /// the sole remaining candidate is selected; with several candidates nothing is selected, so the user must
    /// choose the target again.
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

    /// <summary>
    /// Finds the candidate that is still the very disk the user selected: one unique device for the selection's
    /// unique id (or serial number) whose number, size, name and bus type are unchanged. Returns null when the
    /// disk is gone, ambiguous or changed, so that nothing is shown or erased on the strength of an outdated
    /// selection.
    /// </summary>
    public static WinPeUsbDiskCandidate? FindConfirmed(
        IReadOnlyList<WinPeUsbDiskCandidate> candidates,
        WinPeUsbDiskCandidate selected)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(selected);

        DiskIdentity? resolved = ToIdentity(selected).Resolve(candidates.Select(ToIdentity));
        return resolved is null ? null : candidates.First(candidate => candidate.DiskNumber == resolved.Number);
    }

    private static DiskIdentity ToIdentity(WinPeUsbDiskCandidate disk) => new(
        disk.DiskNumber, disk.UniqueId, disk.SerialNumber, disk.FriendlyName, disk.BusType, disk.SizeBytes);

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
