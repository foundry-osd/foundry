// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Deploy.Services.DriverPacks;

public sealed record MicrosoftUpdateCatalogFirmwareResult
{
    public bool IsUpdateAvailable { get; init; }
    public string DownloadedDirectory { get; init; } = string.Empty;
    public string ExtractedDirectory { get; init; } = string.Empty;
    public string UpdateId { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
}
