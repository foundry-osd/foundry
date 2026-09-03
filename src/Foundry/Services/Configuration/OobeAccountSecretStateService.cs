// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Configuration;

namespace Foundry.Services.Configuration;

internal sealed partial class OobeAccountSecretStateService : IOobeAccountSecretStateService, IDisposable
{
    private readonly OobeAccountSecretState state = new();

    public event EventHandler? Changed;

    public void SetAdministratorPassword(string? value)
    {
        state.SetAdministratorPassword(value);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void SetAdministratorConfirmation(string? value)
    {
        state.SetAdministratorConfirmation(value);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public char[] GetAdministratorPasswordCopy()
    {
        return state.GetAdministratorPasswordCopy();
    }

    public char[] GetAdministratorConfirmationCopy()
    {
        return state.GetAdministratorConfirmationCopy();
    }

    public void SetAdditionalAccountPassword(string accountId, string? value)
    {
        state.SetAdditionalAccountPassword(accountId, value);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void SetAdditionalAccountConfirmation(string accountId, string? value)
    {
        state.SetAdditionalAccountConfirmation(accountId, value);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public char[] GetAdditionalAccountPasswordCopy(string accountId)
    {
        return state.GetAdditionalAccountPasswordCopy(accountId);
    }

    public char[] GetAdditionalAccountConfirmationCopy(string accountId)
    {
        return state.GetAdditionalAccountConfirmationCopy(accountId);
    }

    public OobeAccountConfigurationValidationResult Validate(OobeSettings settings)
    {
        return state.Validate(settings);
    }

    public void Update(OobeSettings settings)
    {
        state.Update(settings);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Clear()
    {
        state.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        state.Dispose();
    }
}
