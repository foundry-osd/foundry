// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Foundry.Connect.Configuration;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Models.Configuration.Deploy;
using Foundry.Core.Services.Security;
using Microsoft.Extensions.Logging;

namespace Foundry.Connect.Security;

/// <summary>
/// Runs the deployment password prompt loop of D11: Deploy's retry delay after each wrong password, cancel → the secrets
/// stay locked for the session (D9), success → every locked secret is decrypted (L4-12) and the key is handed to
/// Bootstrap once (L4-2).
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
            (bool cancelled, byte[]? key) = await PromptAndUnwrapAsync(protection, failedAttempts > 0).ConfigureAwait(false);
            if (cancelled)
            {
                secrets.MarkUnlockDeclined();
                logger.LogInformation("Deployment password prompt was cancelled. Trigger={Trigger}", trigger);
                return ConnectUnlockResult.Cancelled;
            }

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

            try
            {
                int unlockedSecretCount = secrets.Unlock(key);
                logger.LogInformation(
                    "Network secrets unlocked. Trigger={Trigger}, UnlockedSecretCount={UnlockedSecretCount}",
                    trigger,
                    unlockedSecretCount);
                if (!await handoff.TrySendAsync(key, cancellationToken).ConfigureAwait(false))
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
    /// Shows the prompt once and unwraps the key from the typed password, which is zeroed before this returns.
    /// </summary>
    /// <returns>Whether the technician cancelled, and the key, or <see langword="null"/> for a wrong password.</returns>
    private async Task<(bool Cancelled, byte[]? Key)> PromptAndUnwrapAsync(DeployProtectionSettings protection, bool previousAttemptFailed)
    {
        char[]? password = prompt.Prompt(previousAttemptFailed);
        if (password is null)
        {
            return (true, null);
        }

        try
        {
            // Not cancellable: the password is zeroed only once the derivation that reads it has finished.
            return (false, await Task.Run(() => unwrapper.TryUnwrap(protection, password)).ConfigureAwait(false));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(MemoryMarshal.AsBytes(password.AsSpan()));
        }
    }
}
