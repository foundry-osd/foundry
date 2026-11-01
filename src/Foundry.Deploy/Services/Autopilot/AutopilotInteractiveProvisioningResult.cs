// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Deploy.Services.Autopilot;

/// <summary>
/// Offline paths of the <c>Foundry.AutopilotInteractive</c> runtime installed in the applied Windows image.
/// </summary>
/// <param name="RuntimeDirectory">Directory holding the extracted runtime and its configuration.</param>
/// <param name="ExecutablePath">Installed <c>Foundry.AutopilotInteractive.exe</c>.</param>
/// <param name="ConfigPath">Runtime configuration written for the application.</param>
public sealed record AutopilotInteractiveProvisioningResult(string RuntimeDirectory, string ExecutablePath, string ConfigPath);
