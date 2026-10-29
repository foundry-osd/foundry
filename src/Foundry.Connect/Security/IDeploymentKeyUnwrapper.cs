// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration.Deploy;
using Foundry.Core.Services.Security;

namespace Foundry.Connect.Security;

/// <summary>Unwraps the deployment key from the deployment password; a seam over <see cref="DeploymentKeyProtection"/> for tests.</summary>
public interface IDeploymentKeyUnwrapper
{
    /// <summary>Derives the password key and decrypts the password-wrapped deployment key.</summary>
    /// <param name="settings">Protection block of the media.</param>
    /// <param name="password">Deployment password typed by the technician; not modified.</param>
    /// <returns>The 32-byte deployment key, which the caller zeroes; <see langword="null"/> for a wrong password or unusable metadata.</returns>
    byte[]? TryUnwrap(DeployProtectionSettings settings, ReadOnlySpan<char> password);
}

/// <summary>Unwraps the deployment key with the implementation Foundry Deploy uses (L4-4).</summary>
public sealed class DeploymentKeyUnwrapper : IDeploymentKeyUnwrapper
{
    /// <inheritdoc />
    public byte[]? TryUnwrap(DeployProtectionSettings settings, ReadOnlySpan<char> password) =>
        DeploymentKeyProtection.TryUnwrap(settings, password);
}
