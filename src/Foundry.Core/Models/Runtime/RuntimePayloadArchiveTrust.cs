// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Models.Runtime;

/// <summary>
/// Pins the exact original archive provisioned for one application and architecture.
/// </summary>
/// <param name="Application">The Connect or Deploy application name.</param>
/// <param name="RuntimeIdentifier">The Windows runtime identifier.</param>
/// <param name="Sha256">The SHA256 of the complete archive, including dependencies.</param>
public sealed record RuntimePayloadArchiveTrust(string Application, string RuntimeIdentifier, string Sha256);
