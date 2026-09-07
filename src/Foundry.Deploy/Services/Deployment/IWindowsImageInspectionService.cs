// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Deploy.Models;

namespace Foundry.Deploy.Services.Deployment;

public interface IWindowsImageInspectionService
{
    Task<WindowsImageInfo> InspectImageAsync(string imagePath, OperatingSystemCatalogItem selection,
        string workingDirectory, CancellationToken cancellationToken = default);
}
