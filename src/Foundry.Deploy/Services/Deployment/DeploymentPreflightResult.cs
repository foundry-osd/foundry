// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Text.Json.Serialization;
using Foundry.Deploy.Models;

namespace Foundry.Deploy.Services.Deployment;

/// <summary>Distinguishes inspected image bytes from the constrained pre-erasure metadata check.</summary>
public enum ImagePreflightLevel
{
    /// <summary>Authenticated bytes were hashed and their Windows identity was inspected on independent storage.</summary>
    CompleteImageVerified,
    /// <summary>Source availability and metadata were checked; target-backed bytes still require acquisition and inspection.</summary>
    TargetBackedMetadataOnly
}

/// <summary>Records preflight evidence without extending a file lease into later deployment steps.</summary>
public sealed record DeploymentPreflightResult(ImagePreflightLevel Level, long RequiredTargetBytes,
    string? VerifiedImagePath, WindowsImageInfo? Image, string? ConstraintReason)
{
    /// <summary>Binds this in-memory result to the exact selected catalog record; never persists source credentials.</summary>
    [JsonIgnore]
    public OperatingSystemCatalogItem? Selection { get; init; }
}
