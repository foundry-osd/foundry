// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.WinPE.SystemPreparation;

/// <summary>
/// Prepares WinPE network services and system time without making recoverable failures fatal.
/// </summary>
public interface ISystemPreparation
{
    /// <summary>
    /// Starts wired networking and optional WinPE wireless support.
    /// </summary>
    Task PrepareNetworkAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Attempts clock synchronization within a short budget, early in the boot, before the UI process starts. Returns at once
    /// when the clock is already usable.
    /// </summary>
    Task PrepareClockAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Corrects material clock skew and selects the best available timezone.
    /// </summary>
    Task PrepareSystemAsync(CancellationToken cancellationToken);
}
