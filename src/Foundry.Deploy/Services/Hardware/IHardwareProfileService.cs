// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Deploy.Models;

namespace Foundry.Deploy.Services.Hardware;

public interface IHardwareProfileService
{
    Task<HardwareProfile> GetCurrentAsync(CancellationToken cancellationToken = default);
}
