// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Foundry.Connect.Configuration;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Models.Configuration.Deploy;
using Foundry.Core.Services.Runtime;
using Foundry.Core.Services.Security;
using Microsoft.Extensions.Logging;

namespace Foundry.Connect.Security;

/// <summary>
/// Runs the deployment password prompt loop: Deploy's retry delay after each wrong password, cancel → the secrets
/// stay locked for the session, success → every locked secret is decrypted and the key is handed to
/// Bootstrap once when the media carries a key check.
/// </summary>
/// <remarks>
/// The password, its length and the key are never logged. The password is zeroed after each unwrap attempt and the key
/// after the hand-off, on every path. The unwrap runs on the thread pool so the PBKDF2 derivation never blocks the UI
/// thread; the prompt marshals itself to the UI thread.
/// </remarks>
public sealed class ConnectUnlockService(
    FoundryConnectConfigurationDocument configuration,
    ConnectRuntimeSecrets secrets,
    IDeploymentPasswordPrompt prompt,
    IDeploymentKeyUnwrapper unwrapper,
    IDeploymentKeyHandoffSender handoff,
    TimeProvider timeProvider,
    ILogger<ConnectUnlockService> logger) : IConnectUnlockService
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <inheritdoc />
    public bool IsWiredUnlockRequired => secrets.IsWiredLocked && configuration.Dot1x.IsEnabled;

    /// <inheritdoc />
    public bool IsWifiUnlockRequired => secrets.IsWifiLocked;

    /// <inheritdoc />
    public async Task<ConnectUnlockResult> UnlockAsync(ConnectUnlockTrigger trigger, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Read after the gate: a queued call sees the outcome of the prompt it waited behind.
            DeployProtectionSettings? protection = secrets.Protection;
            if (protection is null
                || (!IsWiredUnlockRequired && !IsWifiUnlockRequired)
                || (trigger == ConnectUnlockTrigger.AutomaticWifi && secrets.IsUnlockDeclined))
            {
                return ConnectUnlockResult.NotRequired;
            }

            return await PromptUntilUnlockedAsync(protection, trigger, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<ConnectUnlockResult> PromptUntilUnlockedAsync(
        DeployProtectionSettings protection,
        ConnectUnlockTrigger trigger,
        CancellationToken cancellationToken)
    {
        int failedAttempts = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            char[]? password = TryPrompt(trigger, failedAttempts > 0, out bool isPromptUnavailable);
            if (password is null)
            {
                secrets.MarkUnlockDeclined();
                if (!isPromptUnavailable)
                {
                    logger.LogInformation("Deployment password prompt was cancelled. Trigger={Trigger}", trigger);
                }

                return ConnectUnlockResult.Cancelled;
            }

            byte[]? key = await UnwrapAndZeroPasswordAsync(protection, password).ConfigureAwait(false);
            if (key is null)
            {
                failedAttempts++;
                logger.LogInformation(
                    "Deployment password was rejected. Trigger={Trigger}, FailedAttempt={FailedAttempt}",
                    trigger,
                    failedAttempts);
                await Task.Delay(DeploymentPasswordRetryPolicy.GetDelay(failedAttempts), timeProvider, cancellationToken)
                    .ConfigureAwait(false);
                continue;
            }

            // From here the caller's token no longer applies: the secrets are unlocked whatever happens next.
            try
            {
                if (!TryUnlockSecrets(key, trigger, out int unlockedSecretCount))
                {
                    return ConnectUnlockResult.Cancelled;
                }

                logger.LogInformation(
                    "Network secrets unlocked. Trigger={Trigger}, UnlockedSecretCount={UnlockedSecretCount}",
                    trigger,
                    unlockedSecretCount);
                LogUndecryptableContent();
                // Deploy refuses a handed-off key that it cannot verify, so media without a key check skips the hand-off.
                if (protection.KeyCheck is null || !await TryHandOffAsync(key).ConfigureAwait(false))
                {
                    logger.LogInformation("The deployment key was not handed to Foundry Deploy; Foundry Deploy will ask for the password.");
                }

                return ConnectUnlockResult.Unlocked;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(key);
            }
        }
    }

    /// <summary>
    /// Shows the prompt once. A prompt that cannot be shown (no WPF application, a dispatcher or owner shutting down) is
    /// treated as a cancel, so <see cref="UnlockAsync"/> keeps throwing only <see cref="OperationCanceledException"/>.
    /// </summary>
    /// <returns>The typed password, which the caller zeroes, or <see langword="null"/> when cancelled or unavailable.</returns>
    private char[]? TryPrompt(ConnectUnlockTrigger trigger, bool previousAttemptFailed, out bool isPromptUnavailable)
    {
        isPromptUnavailable = false;
        try
        {
            return prompt.Prompt(previousAttemptFailed);
        }
        catch (InvalidOperationException exception)
        {
            // No exception object: the message is mirrored remotely.
            isPromptUnavailable = true;
            logger.LogWarning(
                "Deployment password prompt could not be shown; the protected network secrets stay locked. Trigger={Trigger}, FailureType={FailureType}",
                trigger,
                exception.GetType().Name);
            return null;
        }
    }

    /// <summary>
    /// Decrypts the locked secrets. A key that fails the media's key check (an inconsistent protection block) unlocks nothing
    /// and is handled like a cancel: the secrets stay locked for the session and the key is not handed off.
    /// </summary>
    private bool TryUnlockSecrets(byte[] key, ConnectUnlockTrigger trigger, out int unlockedSecretCount)
    {
        try
        {
            unlockedSecretCount = secrets.Unlock(key);
            return true;
        }
        catch (ArgumentException)
        {
            unlockedSecretCount = 0;
            secrets.MarkUnlockDeclined();
            logger.LogWarning(
                "The unwrapped deployment key does not match the media's key check; the protected network secrets stay locked. Trigger={Trigger}, FailureCode={FailureCode}",
                trigger,
                "key_check_failed");
            return false;
        }
    }

    /// <summary>
    /// Logs each protected file that the unlocked key could not decrypt (tampered or foreign) once, by its role and exception type
    /// only. Runs after the only successful unlock of the session.
    /// </summary>
    private void LogUndecryptableContent()
    {
        LogIfUndecryptable(secrets.WiredClientCertificate, nameof(ConnectRuntimeSecrets.WiredClientCertificate));
        LogIfUndecryptable(secrets.WifiClientCertificate, nameof(ConnectRuntimeSecrets.WifiClientCertificate));
        LogIfUndecryptable(secrets.WifiEnterpriseProfile, nameof(ConnectRuntimeSecrets.WifiEnterpriseProfile));
    }

    private void LogIfUndecryptable(ConnectProtectedContent content, string role)
    {
        if (content.FailureType is { } failureType)
        {
            logger.LogWarning(
                "A protected network file could not be decrypted with the deployment key. ProtectedContent={ProtectedContent}, FailureType={FailureType}",
                role,
                failureType);
        }
    }

    /// <summary>Unwraps the key from the typed password and zeroes the password on every path.</summary>
    /// <returns>The key, which the caller zeroes, or <see langword="null"/> for a wrong password.</returns>
    private async Task<byte[]?> UnwrapAndZeroPasswordAsync(DeployProtectionSettings protection, char[] password)
    {
        try
        {
            // Not cancellable: the password is zeroed only once the derivation that reads it has finished.
            return await Task.Run(() => unwrapper.TryUnwrap(protection, password)).ConfigureAwait(false);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(MemoryMarshal.AsBytes(password.AsSpan()));
        }
    }

    /// <summary>
    /// Sends the key under its own bounded timeout, not linked to the caller's token (the secrets are already unlocked).
    /// </summary>
    private async Task<bool> TryHandOffAsync(byte[] key)
    {
        using var timeout = new CancellationTokenSource(DeploymentKeyHandoff.ClientTimeout, timeProvider);
        try
        {
            return await handoff.TrySendAsync(key, timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            return false;
        }
    }
}
