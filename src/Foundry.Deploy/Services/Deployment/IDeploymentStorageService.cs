// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Deploy.Services.Deployment;

/// <summary>Probes storage facts without treating unreadable capacity as available space.</summary>
public interface IDeploymentStorageService
{
    /// <summary>Returns available volume bytes, or null when the volume cannot be queried.</summary>
    long? GetAvailableBytes(string path);

    /// <summary>Checks cache write access without changing an existing payload or requiring extra allocation when one is supplied.</summary>
    bool CanWriteDirectory(string path, string? existingFilePath = null);
}
