// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Services.Configuration;

namespace Foundry.Services.Configuration;

internal sealed partial class DeploymentProtectionSecretStateService : IDeploymentProtectionSecretStateService, IDisposable
{
    private readonly DeploymentProtectionSecretState state = new();

    public event EventHandler? Changed;

    public bool HasPassword => state.HasPassword;

    public bool HasConfirmation => state.HasConfirmation;

    public bool IsValid => state.IsValid;

    public bool ShouldRecommendStrongerPassword => state.ShouldRecommendStrongerPassword;

    public void SetPassword(string? value)
    {
        bool wasValid = state.IsValid;
        state.SetPassword(value.AsSpan());
        RaiseChangedIfValidityChanged(wasValid);
    }

    public void SetConfirmation(string? value)
    {
        bool wasValid = state.IsValid;
        state.SetConfirmation(value.AsSpan());
        RaiseChangedIfValidityChanged(wasValid);
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
        bool wasValid = state.IsValid;
        state.Clear();
        RaiseChangedIfValidityChanged(wasValid);
    }

    public void Dispose()
    {
        state.Dispose();
    }

    private void RaiseChangedIfValidityChanged(bool previousValidity)
    {
        if (previousValidity != state.IsValid)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}
