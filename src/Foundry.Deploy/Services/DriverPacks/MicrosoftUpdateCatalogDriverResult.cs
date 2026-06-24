// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Deploy.Services.DriverPacks;

public sealed record MicrosoftUpdateCatalogDriverResult
{
    public required string DestinationDirectory { get; init; }
    public bool IsPayloadAvailable { get; init; }
    public int InfCount { get; init; }
    public IReadOnlyList<MicrosoftUpdateCatalogDownloadedDriver> DownloadedDrivers { get; init; } = Array.Empty<MicrosoftUpdateCatalogDownloadedDriver>();
    public required string Message { get; init; }
}
