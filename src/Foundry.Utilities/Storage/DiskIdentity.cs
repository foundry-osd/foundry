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

        // A duplicate remains ambiguous even if its number, capacity or other facts are unusable.
        List<DiskIdentity> candidates = FindByIdentifier(snapshots);
        return candidates.Count == 1 && Matches(candidates[0]) ? candidates[0] : null;
    }

    /// <summary>
    /// Explains why <see cref="Resolve"/> rejects an inventory using criterion and field names only,
    /// so diagnostics never expose device identifier values. Returns an empty list when the inventory resolves.
    /// </summary>
    public IReadOnlyList<string> DescribeResolutionFailure(IEnumerable<DiskIdentity> snapshots)
    {
        ArgumentNullException.ThrowIfNull(snapshots);
        if (!IsUsable)
        {
            return ["CapturedIdentityUnusable"];
        }

        List<DiskIdentity> candidates = FindByIdentifier(snapshots);
        string identifierField = string.IsNullOrWhiteSpace(UniqueId) ? nameof(SerialNumber) : nameof(UniqueId);
        return candidates.Count switch
        {
            0 => [identifierField + "NotFound"],
            > 1 => [identifierField + "Ambiguous"],
            _ => GetMismatchedFacts(candidates[0])
        };
    }

    /// <summary>
    /// Names the facts that <see cref="Matches"/> would reject, without including their values.
    /// Missing identifiers that were not captured are not reported.
    /// </summary>
    public IReadOnlyList<string> GetMismatchedFacts(DiskIdentity actual)
    {
        ArgumentNullException.ThrowIfNull(actual);
        var mismatches = new List<string>();
        AddMismatch(mismatches, nameof(Number), Number == actual.Number);
        AddMismatch(mismatches, nameof(SizeBytes), SizeBytes == actual.SizeBytes);
        AddMismatch(mismatches, nameof(FriendlyName), EqualFact(FriendlyName, actual.FriendlyName));
        AddMismatch(mismatches, nameof(BusType), EqualFact(BusType, actual.BusType));
        AddMismatch(mismatches, nameof(UniqueId), string.IsNullOrWhiteSpace(UniqueId) || EqualFact(UniqueId, actual.UniqueId));
        AddMismatch(mismatches, nameof(SerialNumber), string.IsNullOrWhiteSpace(SerialNumber) || EqualFact(SerialNumber, actual.SerialNumber));
        return mismatches;
    }

    private List<DiskIdentity> FindByIdentifier(IEnumerable<DiskIdentity> snapshots)
    {
        bool useUniqueId = !string.IsNullOrWhiteSpace(UniqueId);
        string identifier = useUniqueId ? UniqueId : SerialNumber;
        return snapshots
            .Where(snapshot => EqualFact(identifier, useUniqueId ? snapshot.UniqueId : snapshot.SerialNumber))
            .ToList();
    }

    private static void AddMismatch(List<string> mismatches, string fieldName, bool isEqual)
    {
        if (!isEqual)
        {
            mismatches.Add(fieldName);
        }
    }

    private static bool EqualFact(string? expected, string? actual)
    {
        return string.Equals(expected?.Trim() ?? string.Empty, actual?.Trim() ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }
}
