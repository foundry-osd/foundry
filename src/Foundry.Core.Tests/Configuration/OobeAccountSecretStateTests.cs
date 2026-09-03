// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Configuration;

namespace Foundry.Core.Tests.Configuration;

public sealed class OobeAccountSecretStateTests
{
    [Fact]
    public void SetAdditionalAccountPassword_WhenPasswordIsReplaced_InvalidatesPreviousConfirmation()
    {
        using var state = new OobeAccountSecretState();
        state.SetAdditionalAccountPassword("account-1", "Password1!");
        state.SetAdditionalAccountConfirmation("account-1", "Password1!");

        state.SetAdditionalAccountPassword("account-1", "Password2!");

        Assert.False(state.IsAdditionalAccountPasswordConfirmed("account-1"));
        Assert.Equal("Password2!", new string(state.GetAdditionalAccountPasswordCopy("account-1")));
        Assert.Equal("Password1!", new string(state.GetAdditionalAccountConfirmationCopy("account-1")));
    }

    [Fact]
    public void Update_WhenConfigurationRemovesAccountAndDisablesAdministrator_ClearsRemovedSecrets()
    {
        using var state = new OobeAccountSecretState();
        state.SetAdministratorPassword("AdminPass1!");
        state.SetAdministratorConfirmation("AdminPass1!");
        state.SetAdditionalAccountPassword("removed-account", "RemovedPass1!");
        state.SetAdditionalAccountConfirmation("removed-account", "RemovedPass1!");
        state.SetAdditionalAccountPassword("kept-account", "KeptPass1!");
        state.SetAdditionalAccountConfirmation("kept-account", "KeptPass1!");

        state.Update(new OobeSettings
        {
            IsEnabled = true,
            EnableAdministratorAccount = false,
            AdditionalAccounts =
            [
                new OobeAdditionalAccountSettings
                {
                    Id = "kept-account",
                    UserName = "Technician",
                    Type = OobeAccountType.Administrator
                }
            ]
        });

        Assert.Empty(state.GetAdministratorPasswordCopy());
        Assert.Empty(state.GetAdministratorConfirmationCopy());
        Assert.Empty(state.GetAdditionalAccountPasswordCopy("removed-account"));
        Assert.Empty(state.GetAdditionalAccountConfirmationCopy("removed-account"));
        Assert.Equal("KeptPass1!", new string(state.GetAdditionalAccountPasswordCopy("kept-account")));
        Assert.Equal("KeptPass1!", new string(state.GetAdditionalAccountConfirmationCopy("kept-account")));
    }
}
