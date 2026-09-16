// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Deploy.Services.Deployment;

/// <summary>Checks present source access without downloading the image body or claiming authentication.</summary>
public interface IImageSourceProbe
{
    /// <summary>Returns the full source length when advertised; a successful null result has unknown length.</summary>
    Task<long?> ProbeAsync(string sourceUrl, CancellationToken cancellationToken = default);
}
