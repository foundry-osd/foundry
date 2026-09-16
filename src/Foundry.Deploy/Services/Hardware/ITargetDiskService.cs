// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Deploy.Models;

namespace Foundry.Deploy.Services.Hardware;

public interface ITargetDiskService
{
    /// <summary>Enumerates target disks, retaining excluded devices when validating identity uniqueness.</summary>
    Task<IReadOnlyList<TargetDiskInfo>> GetDisksAsync(
        CancellationToken cancellationToken = default,
        bool includeExcludedDisks = false);

    Task<int?> GetDiskNumberForPathAsync(string path, CancellationToken cancellationToken = default);
}
