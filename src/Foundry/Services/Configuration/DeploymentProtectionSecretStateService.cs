// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Services.Configuration;

namespace Foundry.Services.Configuration;

internal sealed partial class DeploymentProtectionSecretStateService : IDeploymentProtectionSecretStateService, IDisposable
{
    private readonly DeploymentProtectionSecretState state = new();

    public bool HasPassword => state.HasPassword;

    public bool HasConfirmation => state.HasConfirmation;

    public bool IsValid => state.IsValid;

    public bool ShouldRecommendStrongerPassword => state.ShouldRecommendStrongerPassword;

    public void SetPassword(string? value)
    {
        state.SetPassword(value.AsSpan());
    }

    public void SetConfirmation(string? value)
    {
        state.SetConfirmation(value.AsSpan());
    }

    public char[] GetPasswordCopy()
    {
        return state.GetPasswordCopy();
    }

    public char[] GetConfirmationCopy()
    {
        return state.GetConfirmationCopy();
    }

    public char[] GetConfirmedPasswordCopy()
    {
        return state.GetConfirmedPasswordCopy();
    }

    public void Clear()
    {
        state.Clear();
    }

    public void Dispose()
    {
        state.Dispose();
    }
}
