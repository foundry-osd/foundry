// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Services.WinPe;

/// <summary>Separates verified data-volume publication from destructive USB provisioning for ordering tests.</summary>
internal interface IWinPeCustomImageMediaPublisher
{
    Task ValidateSourcesAsync(WinPeCustomImageMediaLease package, CancellationToken cancellationToken = default);
    Task ValidateInputDisksAsync(IEnumerable<string> paths, int targetDisk, CancellationToken cancellationToken);
    Task ValidateDestinationDiskAsync(string root, int targetDisk, CancellationToken cancellationToken);
    Task ValidateCapacityAsync(WinPeCustomImageMediaLease package, string root, long additionalBytes, CancellationToken cancellationToken);
    Task PublishAsync(WinPeCustomImageMediaLease package, string destinationRoot, CancellationToken cancellationToken = default,
        IProgress<WinPeMediaProgress>? progress = null);
}
