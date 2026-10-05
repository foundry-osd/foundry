// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;
using Foundry.Core.Models.Configuration.Deploy;
using Foundry.Deploy.Services.DomainJoin;
using Foundry.Deploy.ViewModels;

namespace Foundry.Deploy.Tests;

public sealed class DomainJoinStepViewModelTests
{
    [Theory]
    [InlineData(false, DomainJoinMode.Interactive, false, false)]
    [InlineData(true, DomainJoinMode.Interactive, false, true)]
    [InlineData(true, DomainJoinMode.Automatic, false, false)]
    [InlineData(true, DomainJoinMode.Automatic, true, true)]
    public void StepIsNeededOnlyWhenTheTechnicianHasSomethingToEnter(bool enabled, DomainJoinMode mode, bool technicianChoosesOu, bool expected)
    {
        using var step = new DomainJoinStepViewModel();
        step.Configure(WithOuList(new() { IsEnabled = enabled, Mode = mode }) with { AllowOuSelectionDuringDeployment = technicianChoosesOu });

        Assert.Equal(expected, step.HasInput);
    }

    [Fact]
    public void ZeroTouchOuChoiceForAnotherDomainNeedsNoStep()
    {
        using var step = new DomainJoinStepViewModel();
        step.Configure(WithOuList(new() { IsEnabled = true, Mode = DomainJoinMode.Automatic }) with { OuCatalogDomain = "other.test" });

        Assert.False(step.HasInput);
        Assert.True(step.IsValid);
    }

    [Fact]
    public void InteractiveIsValidOnlyWithDomainQualifiedAccountAndPassword()
    {
        using var step = new DomainJoinStepViewModel();
        step.Configure(new() { IsEnabled = true });
        Assert.False(step.IsValid);

        step.DomainName = "corp.test";
        step.AccountName = "join";
        step.SetPassword("secret");
        Assert.True(step.IsAccountInvalid);
        Assert.False(step.IsValid);

        step.AccountName = "CORP\\join";
        Assert.True(step.IsValid);

        step.SetPassword(default);
        Assert.False(step.IsValid);
    }

    [Fact]
    public void TechnicianChoiceRequiresAListedOuAndPreselectsTheDefault()
    {
        using var step = new DomainJoinStepViewModel();
        step.Configure(WithOuList(new() { IsEnabled = true, Mode = DomainJoinMode.Automatic }) with { DefaultOuId = "sales" });

        Assert.True(step.IsOuListVisible);
        Assert.False(step.IsTypedOuVisible);
        Assert.Equal("OU=Sales,DC=corp,DC=test", step.EffectiveOuDistinguishedName);
        Assert.True(step.IsValid);

        step.SelectedOrganizationalUnit = null;
        Assert.False(step.IsValid);
    }

    [Fact]
    public void ChangingTheDomainReplacesTheSavedListWithATypedOu()
    {
        using var step = new DomainJoinStepViewModel();
        step.Configure(WithOuList(new() { IsEnabled = true }) with { DefaultOuId = "sales" });

        step.DomainName = "other.test";

        Assert.False(step.IsOuListVisible);
        Assert.True(step.IsTypedOuVisible);
        Assert.Null(step.SelectedOrganizationalUnit);
        Assert.Null(step.EffectiveOuDistinguishedName);

        step.TypedOuDistinguishedName = "OU=Sales,DC=corp,DC=test";
        Assert.True(step.IsTypedOuInvalid);

        step.TypedOuDistinguishedName = "OU=Field,DC=other,DC=test";
        Assert.False(step.IsTypedOuInvalid);
        Assert.Equal("OU=Field,DC=other,DC=test", step.EffectiveOuDistinguishedName);
    }

    [Fact]
    public void FixedDefaultIsUsedWhenTheTechnicianCannotChoose()
    {
        using var step = new DomainJoinStepViewModel();
        step.Configure(WithOuList(new() { IsEnabled = true }) with { AllowOuSelectionDuringDeployment = false, DefaultOuId = "sales" });

        Assert.False(step.IsOuListVisible);
        Assert.False(step.IsTypedOuVisible);
        Assert.Equal("OU=Sales,DC=corp,DC=test", step.EffectiveOuDistinguishedName);
    }

    [Fact]
    public void SubmissionCarriesTheInputsAndAnIndependentPasswordCopy()
    {
        using var step = new DomainJoinStepViewModel();
        step.Configure(WithOuList(new() { IsEnabled = true }) with { DefaultOuId = "sales" });
        step.AccountName = "CORP\\join";
        step.SetPassword("secret");

        using DomainJoinSubmission submission = step.CreateSubmission()!;
        step.ClearPassword();

        Assert.Equal("corp.test", submission.DomainName);
        Assert.Equal("CORP\\join", submission.AccountName);
        Assert.Equal("sales", submission.SelectedOuId);
        Assert.Null(submission.TypedOuDistinguishedName);
        Assert.Equal("secret", new string(submission.Password.Span));
        Assert.False(step.HasPassword);
    }

    [Fact]
    public void ZeroTouchSubmissionNeverCarriesAnAccountOrPassword()
    {
        using var step = new DomainJoinStepViewModel();
        step.Configure(WithOuList(new() { IsEnabled = true, Mode = DomainJoinMode.Automatic, AccountName = "CORP\\join" }) with { DefaultOuId = "sales" });
        step.SetPassword("ignored");

        using DomainJoinSubmission submission = step.CreateSubmission()!;

        Assert.Equal(string.Empty, submission.AccountName);
        Assert.True(submission.Password.IsEmpty);
        Assert.Equal("sales", submission.SelectedOuId);
    }

    [Fact]
    public void ReconfiguringDiscardsEarlierInputsAndThePassword()
    {
        using var step = new DomainJoinStepViewModel();
        step.Configure(new() { IsEnabled = true });
        step.DomainName = "corp.test";
        step.AccountName = "CORP\\join";
        step.SetPassword("secret");
        int cleared = 0;
        step.PasswordCleared += (_, _) => cleared++;

        step.Configure(new());

        Assert.Equal(string.Empty, step.DomainName);
        Assert.Equal(string.Empty, step.AccountName);
        Assert.False(step.HasPassword);
        Assert.Equal(1, cleared);
        Assert.Null(step.CreateSubmission());
    }

    private static DeployDomainJoinSettings WithOuList(DeployDomainJoinSettings settings) => settings with
    {
        DomainName = "corp.test",
        OuCatalogDomain = "corp.test",
        AllowOuSelectionDuringDeployment = true,
        OrganizationalUnits = [new() { Id = "sales", DisplayName = "Sales", DistinguishedName = "OU=Sales,DC=corp,DC=test" }]
    };
}
