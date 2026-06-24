// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Services.Autopilot;

/// <summary>
/// Stores the current app-session Microsoft Graph credential used by Autopilot hardware hash onboarding actions.
/// </summary>
public interface IAutopilotHardwareHashGraphSessionService
{
    /// <summary>
    /// Starts or refreshes the interactive Microsoft Graph session.
    /// </summary>
    /// <param name="cancellationToken">Token that cancels authentication.</param>
    /// <returns>A Microsoft Graph access token for the configured hardware hash onboarding scopes.</returns>
    Task<string> ConnectAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets an access token from the current app session without creating a new tenant connection.
    /// </summary>
    /// <param name="cancellationToken">Token that cancels token refresh.</param>
    /// <returns>A Microsoft Graph access token for the configured hardware hash onboarding scopes.</returns>
    Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Clears the current app-session credential and cached token.
    /// </summary>
    void Disconnect();
}
