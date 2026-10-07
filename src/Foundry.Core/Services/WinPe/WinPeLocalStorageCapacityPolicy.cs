// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Utilities.Storage;

namespace Foundry.Core.Services.WinPe;

/// <summary>Applies a minimum free-space guard before preparing boot media on local storage.</summary>
public static class WinPeLocalStorageCapacityPolicy
{
    private const ulong MinimumFreeBytes = 20UL * 1024 * 1024 * 1024;

    /// <summary>Checks each destination before workspace creation or downloads begin.</summary>
    public static WinPeResult Validate(IEnumerable<string> paths, CancellationToken cancellationToken = default)
        => Validate(paths, WindowsVolumeStorage.GetAvailableBytes, cancellationToken);

    /// <summary>Evaluates caller-available capacity through an isolated filesystem query boundary.</summary>
    internal static WinPeResult Validate(IEnumerable<string> paths, Func<string, long> availableBytes,
        CancellationToken cancellationToken = default)
    {
        foreach (string path in paths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                long available = availableBytes(path);
                cancellationToken.ThrowIfCancellationRequested();
                if (available < 0) throw new IOException("Available storage capacity is invalid.");
                if ((ulong)available < MinimumFreeBytes)
                {
                    return WinPeResult.Failure(new WinPeDiagnostic(
                        WinPeErrorCodes.LocalSpaceInsufficient,
                        "There is insufficient free space for boot image preparation.",
                        path, stage: "Validate local storage")
                    {
                        RequiredBytes = MinimumFreeBytes,
                        AvailableBytes = (ulong)available,
                        StoragePath = path
                    });
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or OverflowException)
            {
                return WinPeResult.Failure(new WinPeDiagnostic(
                    WinPeErrorCodes.LocalSpaceUnknown,
                    "The available disk space for boot image preparation could not be verified.",
                    path, stage: "Validate local storage", exception: exception)
                {
                    RequiredBytes = MinimumFreeBytes,
                    StoragePath = path
                });
            }
        }

        return WinPeResult.Success();
    }
}
