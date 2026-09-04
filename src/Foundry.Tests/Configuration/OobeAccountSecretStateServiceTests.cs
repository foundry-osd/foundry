// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;
using Foundry.DependencyInjection;
using Foundry.Services.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Foundry.Tests.Configuration;

public sealed class OobeAccountSecretStateServiceTests
{
    [Fact]
    public void AddFoundryApplicationServices_RegistersOobeAccountSecretStateServiceAsSingleton()
    {
        ServiceProvider provider = new ServiceCollection()
            .AddFoundryApplicationServices()
            .BuildServiceProvider();

        IOobeAccountSecretStateService first = provider.GetRequiredService<IOobeAccountSecretStateService>();
        IOobeAccountSecretStateService second = provider.GetRequiredService<IOobeAccountSecretStateService>();

        Assert.Same(first, second);
    }

    [Fact]
    public void Update_WhenConfigurationRemovesAccountAndDisablesAdministrator_RaisesChangedAndPrunesSecrets()
    {
        ServiceProvider provider = new ServiceCollection()
            .AddFoundryApplicationServices()
            .BuildServiceProvider();
        IOobeAccountSecretStateService service = provider.GetRequiredService<IOobeAccountSecretStateService>();
        int changedCount = 0;
        service.Changed += static (_, _) => { };
        service.Changed += (_, _) => changedCount++;

        service.SetAdministratorPassword("AdminPass1!");
        service.SetAdministratorConfirmation("AdminPass1!");
        service.SetAdditionalAccountPassword("removed-account", "RemovedPass1!");
        service.SetAdditionalAccountConfirmation("removed-account", "RemovedPass1!");
        service.SetAdditionalAccountPassword("kept-account", "KeptPass1!");
        service.SetAdditionalAccountConfirmation("kept-account", "KeptPass1!");

        service.Update(new OobeSettings
        {
            IsEnabled = true,
            EnableAdministratorAccount = false,
            AdditionalAccounts = new[]
            {
                new OobeAdditionalAccountSettings
                {
                    Id = "kept-account",
                    UserName = "Technician",
                    Type = OobeAccountType.Standard,
                    UsePassword = true
                }
            }
        });

        Assert.True(changedCount > 0);
        Assert.Empty(service.GetAdministratorPasswordCopy());
        Assert.Empty(service.GetAdditionalAccountPasswordCopy("removed-account"));
        Assert.Equal("KeptPass1!", new string(service.GetAdditionalAccountPasswordCopy("kept-account")));
    }
}
