// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Deploy.Services.DriverPacks;

public sealed record MicrosoftUpdateCatalogDriverResult
{
    public required string DestinationDirectory { get; init; }
    public bool IsPayloadAvailable { get; init; }
    public int InfCount { get; init; }
    /// <summary>Gets the number of selected payloads transferred during this request.</summary>
    public int DownloadedCount { get; init; }
    /// <summary>Gets the number of selected payloads accepted from the artifact cache.</summary>
    public int ReusedCount { get; init; }
    public IReadOnlyList<MicrosoftUpdateCatalogDownloadedDriver> DownloadedDrivers { get; init; } = Array.Empty<MicrosoftUpdateCatalogDownloadedDriver>();
    public required string Message { get; init; }
}
