// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Deploy.Models.Configuration;

namespace Foundry.Deploy.Services.Security;

public interface IDeploymentProtectionUnlockService
{
    bool TryUnlock(DeployProtectionSettings settings, ReadOnlySpan<char> password);
}
