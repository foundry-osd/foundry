// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Security.Cryptography;
using Foundry.Core.Services.Security;
using Foundry.Deploy.Models.Configuration;
using Foundry.Deploy.Services.Configuration;
using Microsoft.Extensions.Logging;

namespace Foundry.Deploy.Services.Security;

/// <summary>
/// Authorizes Foundry Deploy startup on protected media: accepts the deployment key handed over by Foundry Connect when
/// the media's key check authenticates it, and otherwise prompts for the deployment password with a progressive retry delay.
/// </summary>
/// <remarks>
/// The hand-off is read only when the media requires an unlock, and before the first prompt: Foundry Bootstrap serves the
/// key only until Deploy reports its UI ready, which the password dialog does when it renders. A handed-off key is zeroed
/// on every path; it is never logged.
/// </remarks>
public sealed class DeploymentAccessGate(
    IDeployConfigurationService configurationService,
    IDeploymentProtectionUnlockService unlockService,
    IDeploymentPasswordDialogService passwordDialogService,
    IDeploymentAccessRetryDelay retryDelay,
    IDeploymentKeyHandoffReceiver handoff,
    IDeploymentSecretKeySession session,
    ILogger<DeploymentAccessGate> logger) : IDeploymentAccessGate
{
    public async Task<bool> AuthorizeAsync(CancellationToken cancellationToken = default)
    {
        DeployConfigurationLoadResult loadResult = configurationService.LoadOptional();
        if (loadResult.Exists && loadResult.Document is null)
        {
            return false;
        }

        DeployProtectionSettings protection = loadResult.Document?.Protection ?? new DeployProtectionSettings();
        bool requiresUnlock = DeploymentProtectionDetector.RequiresUnlock(protection);
        if (!requiresUnlock && DeploymentProtectionDetector.HasProtectedArtifacts(loadResult))
        {
            return false;
        }

        if (!requiresUnlock)
        {
            return true;
        }

        if (await TryAcceptHandedOffKeyAsync(protection, cancellationToken))
        {
            return true;
        }

        bool previousAttemptFailed = false;
        int failedAttemptCount = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using DeploymentPasswordPromptResult prompt = passwordDialogService.Prompt(previousAttemptFailed);
            if (!prompt.WasSubmitted)
            {
                return false;
            }

            if (unlockService.TryUnlock(protection, prompt.Password))
            {
                return true;
            }

            previousAttemptFailed = true;
            failedAttemptCount++;
            await retryDelay.WaitAsync(failedAttemptCount, cancellationToken);
        }
    }

    // Media without a key check (built before it) never accept a handed-off key and prompt as before.
    private async Task<bool> TryAcceptHandedOffKeyAsync(DeployProtectionSettings protection, CancellationToken cancellationToken)
    {
        byte[]? key = await handoff.TryReceiveAsync(cancellationToken);
        if (key is null)
        {
            return false;
        }

        try
        {
            if (DeploymentKeyProtection.VerifyKeyCheck(protection.KeyCheck, key))
            {
                // SetKey copies the key into the session; the received buffer is zeroed below.
                session.SetKey(key);
                logger.LogInformation("Deployment unlocked with the key handed over by Foundry Connect.");
                return true;
            }

            logger.LogWarning(
                "The deployment key handed over by Foundry Connect was rejected. HasKeyCheck={HasKeyCheck}",
                protection.KeyCheck is not null);
            return false;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }
}
