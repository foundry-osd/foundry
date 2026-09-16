// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Utilities.Storage;

/// <summary>
/// Captures stable device identifiers and the disk facts confirmed by a caller.
/// </summary>
public sealed record DiskIdentity(
    int Number,
    string UniqueId,
    string SerialNumber,
    string FriendlyName,
    string BusType,
    ulong SizeBytes)
{
    /// <summary>
    /// Gets whether this snapshot has a valid disk number, capacity and at least one device identifier.
    /// This does not establish uniqueness or workflow eligibility.
    /// </summary>
    public bool IsUsable => Number >= 0 && SizeBytes > 0 &&
                            (!string.IsNullOrWhiteSpace(UniqueId) || !string.IsNullOrWhiteSpace(SerialNumber));

    /// <summary>
    /// Captures identity without using mutable partition metadata or altering the supplied facts.
    /// </summary>
    public static DiskIdentity FromDiskInfo(DiskInfo disk)
    {
        ArgumentNullException.ThrowIfNull(disk);
        return new DiskIdentity(disk.Number, disk.UniqueId, disk.SerialNumber, disk.FriendlyName, disk.BusType, disk.SizeBytes);
    }

    /// <summary>
    /// Requires the original number and fingerprint, including every originally available identifier.
    /// A captured unique identifier cannot fall back to a serial number when it disappears.
    /// </summary>
    public bool Matches(DiskIdentity actual)
    {
        return IsUsable && actual is { IsUsable: true } &&
               Number == actual.Number && SizeBytes == actual.SizeBytes &&
               EqualFact(FriendlyName, actual.FriendlyName) && EqualFact(BusType, actual.BusType) &&
               (string.IsNullOrWhiteSpace(UniqueId) || EqualFact(UniqueId, actual.UniqueId)) &&
               (string.IsNullOrWhiteSpace(SerialNumber) || EqualFact(SerialNumber, actual.SerialNumber));
    }

    /// <summary>
    /// Resolves one unique device across the complete inventory before checking its number and fingerprint.
    /// Uses the captured unique identifier when available, otherwise the captured serial number.
    /// </summary>
    public DiskIdentity? Resolve(IEnumerable<DiskIdentity> snapshots)
    {
        ArgumentNullException.ThrowIfNull(snapshots);
        if (!IsUsable)
        {
            return null;
        }

        bool useUniqueId = !string.IsNullOrWhiteSpace(UniqueId);
        string identifier = useUniqueId ? UniqueId : SerialNumber;
        DiskIdentity? candidate = null;
        foreach (DiskIdentity snapshot in snapshots)
        {
            if (!EqualFact(identifier, useUniqueId ? snapshot.UniqueId : snapshot.SerialNumber))
            {
                continue;
            }

            // A duplicate remains ambiguous even if its number, capacity or other facts are unusable.
            if (candidate is not null)
            {
                return null;
            }

            candidate = snapshot;
        }

        return candidate is not null && Matches(candidate) ? candidate : null;
    }

    private static bool EqualFact(string? expected, string? actual)
    {
        return string.Equals(expected?.Trim() ?? string.Empty, actual?.Trim() ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }
}
