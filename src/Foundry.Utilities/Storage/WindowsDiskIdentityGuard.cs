// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Foundry.Utilities.Storage;

/// <summary>
/// Creates a PowerShell guard for the last identity check before a disk operation.
/// </summary>
public static class WindowsDiskIdentityGuard
{
    /// <summary>
    /// Identifies a failed guard without including device identifiers in the failure.
    /// </summary>
    public const string FailureMarker = "FOUNDRY_DISK_IDENTITY_MISMATCH";

    /// <summary>
    /// Creates statements that re-enumerate disks and assign the original disk object to
    /// <c>$foundryConfirmedDisk</c> only when <see cref="DiskIdentity.Resolve"/> would accept it.
    /// Callers own eligibility checks and operations; this check does not lock the physical device.
    /// </summary>
    public static string CreateScript(DiskIdentity expected)
    {
        ArgumentNullException.ThrowIfNull(expected);
        if (!expected.IsUsable)
        {
            return $"$foundryConfirmedDisk = $null; throw '{FailureMarker}'";
        }

        // Base64 carries UTF-8 JSON as data, including quotes and PowerShell metacharacters.
        string encodedIdentity = Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            expected.Number,
            expected.UniqueId,
            expected.SerialNumber,
            expected.FriendlyName,
            expected.BusType,
            Size = expected.SizeBytes.ToString(CultureInfo.InvariantCulture)
        })));

        return $$"""
            $foundryConfirmedDisk = $null
            $foundryConfirmedDisk = & {
                try {
                    function Test-FoundryIdentityFact([string]$expectedFact, [string]$actualFact) {
                        return [string]::Equals($expectedFact.Trim(), $actualFact.Trim(), [StringComparison]::OrdinalIgnoreCase)
                    }
                    $foundryExpected = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('{{encodedIdentity}}')) | ConvertFrom-Json
                    $foundryPrimaryProperty = 'UniqueId'
                    if ([string]::IsNullOrWhiteSpace($foundryExpected.UniqueId)) {
                        $foundryPrimaryProperty = 'SerialNumber'
                    }
                    $foundryMatches = @(foreach ($foundryDisk in @(Get-Disk -ErrorAction Stop)) {
                        if (Test-FoundryIdentityFact $foundryExpected.$foundryPrimaryProperty $foundryDisk.$foundryPrimaryProperty) {
                            $foundryDisk
                        }
                    })
                    if ($foundryMatches.Count -ne 1) { throw '{{FailureMarker}}' }
                    $foundryCandidate = $foundryMatches[0]
                    $foundryNumber = 0
                    $foundrySize = [uint64]0
                    if (-not [int]::TryParse([string]$foundryCandidate.Number, [Globalization.NumberStyles]::Integer, [Globalization.CultureInfo]::InvariantCulture, [ref]$foundryNumber) -or
                        -not [uint64]::TryParse([string]$foundryCandidate.Size, [Globalization.NumberStyles]::Integer, [Globalization.CultureInfo]::InvariantCulture, [ref]$foundrySize) -or
                        $foundryNumber -ne $foundryExpected.Number -or $foundrySize -ne [uint64]$foundryExpected.Size -or
                        -not (Test-FoundryIdentityFact $foundryExpected.FriendlyName $foundryCandidate.FriendlyName) -or
                        -not (Test-FoundryIdentityFact $foundryExpected.BusType $foundryCandidate.BusType) -or
                        (-not [string]::IsNullOrWhiteSpace($foundryExpected.UniqueId) -and -not (Test-FoundryIdentityFact $foundryExpected.UniqueId $foundryCandidate.UniqueId)) -or
                        (-not [string]::IsNullOrWhiteSpace($foundryExpected.SerialNumber) -and -not (Test-FoundryIdentityFact $foundryExpected.SerialNumber $foundryCandidate.SerialNumber))) {
                        throw '{{FailureMarker}}'
                    }
                    return $foundryCandidate
                } catch {
                    throw '{{FailureMarker}}'
                }
            }
            """;
    }
}
