// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Utilities.Hardware;

/// <summary>
/// Inspects hardware facts for the current machine.
/// </summary>
public interface IHardwareInspector
{
    /// <summary>
    /// Gets the current hardware snapshot.
    /// </summary>
    Task<HardwareSnapshot> GetCurrentAsync(CancellationToken cancellationToken = default);
}
