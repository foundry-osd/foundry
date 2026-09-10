// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Bootstrap.SystemPreparation;

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
    /// Corrects material clock skew and selects the best available timezone.
    /// </summary>
    Task PrepareSystemAsync(CancellationToken cancellationToken);
}
