// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Autopilot;

namespace Foundry.Services.Autopilot;

/// <summary>
/// Represents app registration certificate state after removing a selected credential.
/// </summary>
public sealed record AutopilotCertificateRemovalResult
{
    /// <summary>
    /// Gets updated hardware hash upload settings after certificate removal.
    /// </summary>
    public AutopilotHardwareHashUploadSettings Settings { get; init; } = new();

    /// <summary>
    /// Gets the app registration certificate credentials after removal.
    /// </summary>
    public IReadOnlyList<AutopilotGraphKeyCredential> Certificates { get; init; } = [];
}
