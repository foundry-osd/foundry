// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Configuration;

namespace Foundry.Core.Tests.Configuration;

public sealed class DomainJoinLocalAccountAdvisoryTests
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    public void AJoinWithoutAnyLocalAccountIsReported(bool oobeEnabled, bool administrator, bool additionalAccount)
    {
        Assert.True(DomainJoinLocalAccountAdvisory.IsLocalAccountMissing(Document(true, oobeEnabled, administrator, additionalAccount)));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void AnAdministratorOrAnAdditionalAccountIsEnough(bool administrator, bool additionalAccount)
    {
        Assert.False(DomainJoinLocalAccountAdvisory.IsLocalAccountMissing(Document(true, true, administrator, additionalAccount)));
    }

    [Fact]
    public void AccountsOfADisabledOobeCustomizationAreNotWritten()
    {
        Assert.True(DomainJoinLocalAccountAdvisory.IsLocalAccountMissing(Document(true, oobeEnabled: false, administrator: true, additionalAccount: true)));
    }

    [Fact]
    public void NothingIsReportedWithoutDomainJoin()
    {
        Assert.False(DomainJoinLocalAccountAdvisory.IsLocalAccountMissing(Document(false, false, false, false)));
    }

    [Fact]
    public void ACustomAnswerFileUsedByDefaultOwnsItsAccounts()
    {
        FoundryConfigurationDocument document = Document(true, false, false, false);
        var file = new UnattendFileSettings { Id = new string('a', 32) };

        Assert.False(DomainJoinLocalAccountAdvisory.IsLocalAccountMissing(
            document with { Unattend = new() { IsEnabled = true, DefaultFileId = file.Id, Files = [file] } }));
        // Native settings stay the default, so Foundry still writes the answer file.
        Assert.True(DomainJoinLocalAccountAdvisory.IsLocalAccountMissing(
            document with { Unattend = new() { IsEnabled = true, Files = [file] } }));
        Assert.True(DomainJoinLocalAccountAdvisory.IsLocalAccountMissing(
            document with { Unattend = new() { IsEnabled = false, DefaultFileId = file.Id, Files = [file] } }));
    }

    private static FoundryConfigurationDocument Document(bool domainJoin, bool oobeEnabled, bool administrator, bool additionalAccount) => new()
    {
        DomainJoin = new() { IsEnabled = domainJoin },
        Customization = new()
        {
            Oobe = new()
            {
                IsEnabled = oobeEnabled,
                EnableAdministratorAccount = administrator,
                AdditionalAccounts = additionalAccount ? [new() { Id = "one", UserName = "local" }] : []
            }
        }
    };
}
