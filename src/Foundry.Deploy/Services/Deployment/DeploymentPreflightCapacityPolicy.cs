// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Deploy.Models;

namespace Foundry.Deploy.Services.Deployment;

/// <summary>Calculates a conservative target requirement without claiming that source bytes or metadata are verified.</summary>
public static class DeploymentPreflightCapacityPolicy
{
    private const long MiB = 1024L * 1024;
    private const long GiB = 1024 * MiB;

    // These partitions match TargetDiskPreparationScript; the layout is not changed by preflight.
    public const long EfiPartitionBytes = 260 * MiB;
    public const long MsrPartitionBytes = 16 * MiB;
    public const long RecoveryPartitionBytes = 5 * GiB;
    public const long ReservedPartitionBytes = EfiPartitionBytes + MsrPartitionBytes + RecoveryPartitionBytes;

    // Windows 11 requires a 64 GB device. Foundry conservatively reserves 64 GiB for Windows itself.
    // https://learn.microsoft.com/en-us/windows/whats-new/windows-11-requirements
    public const long WindowsMinimumCapacityBytes = 64 * GiB;

    // Foundry planning allowances, not measured image sizes or Microsoft installation guarantees.
    public const long UnknownExpandedImageReserveBytes = 64 * GiB;
    public const long ScratchAndSourceReserveBytes = 16 * GiB;

    /// <summary>
    /// Includes partition overhead, expanded Windows and working space, target-backed source bytes and known payloads.
    /// Unknown target-backed compressed size is rejected; unknown expanded size uses a conservative planning reserve.
    /// The caller must separately validate the selection and recheck capacity after acquisition and formatting.
    /// </summary>
    public static long CalculateRequiredTargetBytes(OperatingSystemCatalogItem selection, WindowsImageInfo? image,
        bool usesTargetStorage, long selectedAdditionalPayloadBytes = 0)
    {
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentOutOfRangeException.ThrowIfNegative(selection.SizeBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(selectedAdditionalPayloadBytes);
        long expandedSize = image?.ExpandedSizeBytes ?? 0;
        ArgumentOutOfRangeException.ThrowIfNegative(expandedSize);
        if (usesTargetStorage && selection.SizeBytes == 0)
        {
            throw new InvalidOperationException("Target-backed image acquisition requires a known compressed image size before disk preparation.");
        }

        if (expandedSize == 0) expandedSize = UnknownExpandedImageReserveBytes;
        checked
        {
            long windowsRequirement = Math.Max(WindowsMinimumCapacityBytes, expandedSize + ScratchAndSourceReserveBytes);
            long targetSourceBytes = usesTargetStorage ? selection.SizeBytes : 0;
            return ReservedPartitionBytes + windowsRequirement + targetSourceBytes + selectedAdditionalPayloadBytes;
        }
    }
}
