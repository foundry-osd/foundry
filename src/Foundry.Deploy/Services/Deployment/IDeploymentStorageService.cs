// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Deploy.Services.Deployment;

/// <summary>Probes storage facts without treating unreadable capacity as available space.</summary>
public interface IDeploymentStorageService
{
    /// <summary>Returns available volume bytes, or null when the volume cannot be queried.</summary>
    long? GetAvailableBytes(string path);

    /// <summary>Creates the cache directory and checks write access using a disposable temporary file.</summary>
    bool CanWriteDirectory(string path);
}
