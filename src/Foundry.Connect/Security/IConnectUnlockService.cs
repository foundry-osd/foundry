// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Connect.Security;

/// <summary>Why Foundry Connect asks for the deployment password; logged with every unlock outcome.</summary>
public enum ConnectUnlockTrigger
{
    /// <summary>Wired 802.1X needs a locked secret at startup, before provisioning runs.</summary>
    WiredStartup,

    /// <summary>Ethernet gives no internet and the automatic provisioned Wi-Fi connection needs a locked secret.</summary>
    AutomaticWifi,

    /// <summary>The technician selected Connect on the locked provisioned Wi-Fi.</summary>
    ManualWifi
}

/// <summary>Outcome of <see cref="IConnectUnlockService.UnlockAsync"/>.</summary>
public enum ConnectUnlockResult
{
    /// <summary>
    /// No prompt was shown: the media has no protection block, no secret that a feature needs is locked (a locked wired
    /// secret counts only while 802.1X is enabled, see <see cref="IConnectUnlockService.IsWiredUnlockRequired"/> and
    /// <see cref="IConnectUnlockService.IsWifiUnlockRequired"/>), or an automatic request follows a cancel.
    /// </summary>
    NotRequired,

    /// <summary>The password was accepted: every locked secret was decrypted and the key was offered to Bootstrap once.</summary>
    Unlocked,

    /// <summary>
    /// The technician cancelled the prompt, the prompt could not be shown, or the unwrapped key failed the media key check:
    /// the secrets stay locked for the session and are not prompted for again automatically.
    /// </summary>
    Cancelled
}

/// <summary>
/// Unlocks the network secrets protected by the deployment key with the deployment password, then hands the key to
/// Foundry Bootstrap once so Foundry Deploy does not ask again.
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
    /// than <see cref="ConnectUnlockResult.NotRequired"/> means the prompt was opened (or could not be, for
    /// <see cref="ConnectUnlockResult.Cancelled"/>), and the returned task stays pending while it is open. The password and
    /// the key are zeroed on every path and never logged.
    /// <para>
    /// When the gate is free, the first prompt runs synchronously inside this call, before it returns a task: called on the
    /// UI thread, the modal dialog runs a nested message loop there, so the caller must not hold UI state that a message
    /// handled meanwhile could change unexpectedly.
    /// </para>
    /// <para>
    /// Once the password is accepted, the outcome is <see cref="ConnectUnlockResult.Unlocked"/> even if
    /// <paramref name="cancellationToken"/> is cancelled meanwhile: the hand-off to Bootstrap runs under its own bounded
    /// timeout, not under the caller's token.
    /// </para>
    /// </remarks>
    /// <param name="trigger">Why the password is needed.</param>
    /// <param name="cancellationToken">Cancels a queued call or a retry delay; it does not close an open prompt.</param>
    /// <returns>The unlock outcome.</returns>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> was cancelled before the password was accepted; the only exception thrown,
    /// since a prompt that cannot be shown, the unwrap and the hand-off report every failure as a result.
    /// </exception>
    Task<ConnectUnlockResult> UnlockAsync(ConnectUnlockTrigger trigger, CancellationToken cancellationToken);
}
