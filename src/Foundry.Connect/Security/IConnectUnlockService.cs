// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Connect.Security;

/// <summary>Why Foundry Connect asks for the deployment password; logged with every unlock outcome.</summary>
public enum ConnectUnlockTrigger
{
    /// <summary>Wired 802.1X needs a locked secret at startup, before provisioning runs (L4-12).</summary>
    WiredStartup,

    /// <summary>Ethernet gives no internet and the automatic provisioned Wi-Fi connection needs a locked secret (L4-11).</summary>
    AutomaticWifi,

    /// <summary>The technician selected Connect on the locked provisioned Wi-Fi.</summary>
    ManualWifi
}

/// <summary>Outcome of <see cref="IConnectUnlockService.UnlockAsync"/>.</summary>
public enum ConnectUnlockResult
{
    /// <summary>No prompt was shown: nothing is locked, the media has no protection block, or an automatic request follows a cancel.</summary>
    NotRequired,

    /// <summary>The password was accepted: every locked secret was decrypted and the key was offered to Bootstrap once.</summary>
    Unlocked,

    /// <summary>The technician cancelled the prompt: the secrets stay locked for the session (D9, L4-10).</summary>
    Cancelled
}

/// <summary>
/// Unlocks the network secrets protected by the deployment key with the deployment password (D11), then hands the key to
/// Foundry Bootstrap once so Foundry Deploy does not ask again (L4-2).
/// </summary>
public interface IConnectUnlockService
{
    /// <summary>Gets whether wired 802.1X is enabled and one of its secrets is still locked.</summary>
    bool IsWiredUnlockRequired { get; }

    /// <summary>Gets whether a provisioned Wi-Fi secret is still locked.</summary>
    bool IsWifiUnlockRequired { get; }

    /// <summary>
    /// Prompts for the deployment password until it unwraps the deployment key or the technician cancels; each wrong
    /// password waits Deploy's retry delay before the prompt reopens with the error text.
    /// </summary>
    /// <remarks>
    /// Calls are serialized: a call queued behind a successful unlock returns <see cref="ConnectUnlockResult.NotRequired"/>,
    /// and an <see cref="ConnectUnlockTrigger.AutomaticWifi"/> call after a cancel never reopens the prompt. A result other
    /// than <see cref="ConnectUnlockResult.NotRequired"/> means the prompt was shown, and the returned task stays pending
    /// while it is open. The password and the key are zeroed on every path and never logged.
    /// </remarks>
    /// <param name="trigger">Why the password is needed.</param>
    /// <param name="cancellationToken">Cancels a queued call or a retry delay; it does not close an open prompt.</param>
    /// <returns>The unlock outcome.</returns>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> was cancelled; the only exception thrown, since the unwrap and the hand-off
    /// report every failure as a result.
    /// </exception>
    Task<ConnectUnlockResult> UnlockAsync(ConnectUnlockTrigger trigger, CancellationToken cancellationToken);
}
