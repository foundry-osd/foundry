// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Deploy.Models.Configuration;

namespace Foundry.Deploy.Services.Startup;

/// <summary>The outcome of one Autopilot group tag discovery.</summary>
/// <param name="Document">
/// The configuration with the discovered tags as known group tags, or the configuration unchanged when the discovery did not
/// run or failed.
/// </param>
/// <param name="Failed">
/// Whether the discovery ran and failed or timed out, so the configured tags were kept; <see langword="false"/> when it succeeded
/// or the configuration does not discover tags.
/// </param>
public sealed record AutopilotGroupTagRefresh(FoundryDeployConfigurationDocument Document, bool Failed);
