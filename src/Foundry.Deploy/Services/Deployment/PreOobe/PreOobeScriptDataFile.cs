// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Deploy.Services.Deployment.PreOobe;

/// <summary>
/// Describes a generated data file staged next to pre-OOBE scripts.
/// </summary>
public sealed record PreOobeScriptDataFile
{
    /// <summary>
    /// Gets the target relative path written under the staged pre-OOBE data folder.
    /// </summary>
    public required string FileName { get; init; }

    /// <summary>
    /// Gets the UTF-8 file content.
    /// </summary>
    public string Content { get; init; } = string.Empty;

    /// <summary>
    /// Gets binary file content when the data file is not text.
    /// </summary>
    public byte[]? Bytes { get; init; }

    /// <summary>
    /// Gets whether the data file contains transient sensitive material.
    /// </summary>
    public bool IsSensitive { get; init; }

    /// <summary>Gets the action that owns this input and its cleanup.</summary>
    public string OwningActionId { get; init; } = string.Empty;

    /// <summary>Gets the retention rule, independent of failures in other actions.</summary>
    public PreOobeCleanupDisposition CleanupDisposition { get; init; } = PreOobeCleanupDisposition.RetainForRetry;
}

/// <summary>Defines when a staged input can remain on the target.</summary>
public enum PreOobeCleanupDisposition
{
    /// <summary>Remove on every outcome, including skipped or unstarted actions.</summary>
    SecretAlways,
    /// <summary>Remove when the owning action succeeds.</summary>
    AfterSuccess,
    /// <summary>Retain on failure for an explicit bounded retry.</summary>
    RetainForRetry
}
