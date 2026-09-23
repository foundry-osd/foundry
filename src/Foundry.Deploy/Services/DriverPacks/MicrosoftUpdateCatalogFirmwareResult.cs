// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Deploy.Services.DriverPacks;

public sealed record MicrosoftUpdateCatalogFirmwareResult
{
    public bool IsUpdateAvailable { get; init; }
    public string DownloadedDirectory { get; init; } = string.Empty;
    /// <summary>Gets the number of selected payloads transferred during this request.</summary>
    public int DownloadedCount { get; init; }
    /// <summary>Gets the number of selected payloads accepted from the artifact cache.</summary>
    public int ReusedCount { get; init; }
    public string UpdateId { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
}
