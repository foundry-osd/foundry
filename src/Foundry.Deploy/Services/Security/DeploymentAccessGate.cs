// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Deploy.Models.Configuration;
using Foundry.Deploy.Services.Configuration;

namespace Foundry.Deploy.Services.Security;

public sealed class DeploymentAccessGate(
    IDeployConfigurationService configurationService,
    IDeploymentProtectionUnlockService unlockService,
    IDeploymentPasswordDialogService passwordDialogService,
    IDeploymentAccessRetryDelay retryDelay) : IDeploymentAccessGate
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
}
