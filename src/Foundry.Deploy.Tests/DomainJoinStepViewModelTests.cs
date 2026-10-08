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
        step.Configure(OneDomain(mode) with { IsEnabled = enabled, AllowOuSelectionDuringDeployment = technicianChoosesOu });

        Assert.Equal(expected, step.HasInput);
    }

    [Fact]
    public void ZeroTouchStepIsSkippedWhenThereIsNothingToChoose()
    {
        using var step = new DomainJoinStepViewModel();
        DeployDomainJoinSettings settings = TwoDomains(DomainJoinMode.Automatic) with { AllowOuSelectionDuringDeployment = false };
        step.Configure(settings);

        Assert.False(step.HasInput);
        Assert.True(step.IsValid);
        Assert.Equal("corp.test", step.DomainName);
        Assert.Equal("OU=Sales,DC=corp,DC=test", step.EffectiveOuDistinguishedName);
    }

    [Fact]
    public void ZeroTouchStepIsShownForADomainChoiceAlone()
    {
        using var step = new DomainJoinStepViewModel();
        step.Configure(TwoDomains(DomainJoinMode.Automatic) with { AllowDomainSelectionDuringDeployment = true, AllowOuSelectionDuringDeployment = false });

        Assert.True(step.HasInput);
        Assert.True(step.IsDomainListVisible);
        Assert.False(step.IsDomainTextVisible);
        Assert.True(step.IsValid);
    }

    [Theory]
    [InlineData(1, true, false)]
    [InlineData(2, false, false)]
    [InlineData(2, true, true)]
    public void DomainListIsOfferedOnlyWhenChoiceIsAllowedAndSeveralDomainsExist(int domainCount, bool allowChoice, bool expected)
    {
        using var step = new DomainJoinStepViewModel();
        DeployDomainJoinSettings settings = (domainCount == 1 ? OneDomain(DomainJoinMode.Interactive) : TwoDomains(DomainJoinMode.Interactive)) with
        { AllowDomainSelectionDuringDeployment = allowChoice };
        step.Configure(settings);

        Assert.Equal(expected, step.IsDomainListVisible);
        Assert.True(step.IsDomainReadOnly);
        Assert.Equal("corp.test", step.DomainName);
    }

    [Fact]
    public void ChangingTheDomainOffersItsOusAndPreselectsItsDefault()
    {
        using var step = new DomainJoinStepViewModel();
        DeployDomainJoinSettings settings = TwoDomains(DomainJoinMode.Interactive) with { AllowDomainSelectionDuringDeployment = true };
        settings = settings with
        {
            Domains =
            [
                settings.Domains[0],
                settings.Domains[1] with
                {
                    DefaultOuId = "kiosk",
                    OrganizationalUnits = [new() { Id = "kiosk", DisplayName = "Kiosks", DistinguishedName = "OU=Kiosks,DC=emea,DC=test" }]
                }
            ]
        };
        step.Configure(settings);
        Assert.Equal("sales", step.SelectedOrganizationalUnit?.Id);

        step.SelectedDomain = step.Domains[1];

        Assert.Equal("emea.test", step.DomainName);
        Assert.Equal("kiosk", step.SelectedOrganizationalUnit?.Id);
        Assert.All(step.OrganizationalUnits, unit => Assert.EndsWith("DC=emea,DC=test", unit.DistinguishedName));
        Assert.Equal("OU=Kiosks,DC=emea,DC=test", step.EffectiveOuDistinguishedName);
    }

    [Fact]
    public void ChangingTheDomainKeepsTheTypedAccountAndPassword()
    {
        using var step = new DomainJoinStepViewModel();
        step.Configure(TwoDomains(DomainJoinMode.Interactive) with { AllowDomainSelectionDuringDeployment = true });
        step.AccountName = "CORP\\join";
        step.SetPassword("secret");

        step.SelectedDomain = step.Domains[1];

        Assert.Equal("CORP\\join", step.AccountName);
        Assert.True(step.HasPassword);
    }

    [Theory]
    [InlineData(DomainJoinMode.Automatic)]
    [InlineData(DomainJoinMode.Interactive)]
    public void OuChoiceOnADomainWithoutOusDoesNotBlock(DomainJoinMode mode)
    {
        using var step = new DomainJoinStepViewModel();
        step.Configure(TwoDomains(mode) with { AllowDomainSelectionDuringDeployment = true });
        step.AccountName = "EMEA\\tech";
        step.SetPassword("secret");

        step.SelectedDomain = step.Domains[1];

        Assert.False(step.IsOuListVisible);
        Assert.Equal(mode == DomainJoinMode.Interactive, step.IsTypedOuVisible);
        Assert.Null(step.EffectiveOuDistinguishedName);
        Assert.True(step.IsValid);
    }

    [Fact]
    public void InteractiveWithListedDomainsHidesTheFreeDomainEntry()
    {
        using var step = new DomainJoinStepViewModel();
        step.Configure(OneDomain(DomainJoinMode.Interactive));

        Assert.True(step.IsDomainTextVisible);
        Assert.True(step.IsDomainReadOnly);
        Assert.False(step.IsDomainInvalid);
    }

    [Fact]
    public void InteractiveWithoutListedDomainsIsValidOnlyWithDomainQualifiedAccountAndPassword()
    {
        using var step = new DomainJoinStepViewModel();
        step.Configure(new() { IsEnabled = true });
        Assert.False(step.IsDomainReadOnly);
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
    public void TypedOuMustBeAnOuOfTheTypedDomain()
    {
        using var step = new DomainJoinStepViewModel();
        step.Configure(new() { IsEnabled = true });
        step.DomainName = "other.test";

        Assert.True(step.IsTypedOuVisible);
        step.TypedOuDistinguishedName = "OU=Sales,DC=corp,DC=test";
        Assert.True(step.IsTypedOuInvalid);

        step.TypedOuDistinguishedName = "CN=Computers,DC=other,DC=test";
        Assert.True(step.IsTypedOuInvalid);

        step.TypedOuDistinguishedName = "OU=Field,DC=other,DC=test";
        Assert.False(step.IsTypedOuInvalid);
        Assert.Equal("OU=Field,DC=other,DC=test", step.EffectiveOuDistinguishedName);
    }

    [Fact]
    public void TechnicianChoiceRequiresAListedOuAndPreselectsTheDefault()
    {
        using var step = new DomainJoinStepViewModel();
        step.Configure(OneDomain(DomainJoinMode.Automatic));

        Assert.True(step.IsOuListVisible);
        Assert.False(step.IsTypedOuVisible);
        Assert.Equal("OU=Sales,DC=corp,DC=test", step.EffectiveOuDistinguishedName);
        Assert.True(step.IsValid);

        step.SelectedOrganizationalUnit = null;
        Assert.False(step.IsValid);
    }

    [Fact]
    public void FixedDefaultIsUsedWhenTheTechnicianCannotChoose()
    {
        using var step = new DomainJoinStepViewModel();
        step.Configure(OneDomain(DomainJoinMode.Interactive) with { AllowOuSelectionDuringDeployment = false });

        Assert.False(step.IsOuListVisible);
        Assert.False(step.IsTypedOuVisible);
        Assert.Equal("OU=Sales,DC=corp,DC=test", step.EffectiveOuDistinguishedName);
    }

    [Fact]
    public void SubmissionCarriesTheSelectedDomainTheInputsAndAnIndependentPasswordCopy()
    {
        using var step = new DomainJoinStepViewModel();
        step.Configure(TwoDomains(DomainJoinMode.Interactive) with { AllowDomainSelectionDuringDeployment = true });
        step.AccountName = "CORP\\join";
        step.SetPassword("secret");

        using DomainJoinSubmission first = step.CreateSubmission()!;
        step.SelectedDomain = step.Domains[1];
        using DomainJoinSubmission second = step.CreateSubmission()!;
        step.ClearPassword();

        Assert.Equal("corp", first.SelectedDomainId);
        Assert.Equal("corp.test", first.DomainName);
        Assert.Equal("CORP\\join", first.AccountName);
        Assert.Equal("sales", first.SelectedOuId);
        Assert.Null(first.TypedOuDistinguishedName);
        Assert.Equal("secret", new string(first.Password.Span));
        Assert.Equal("emea", second.SelectedDomainId);
        Assert.Equal("emea.test", second.DomainName);
        Assert.Null(second.SelectedOuId);
        Assert.False(step.HasPassword);
    }

    [Fact]
    public void SubmissionWithoutListedDomainsCarriesTheTypedDomainOnly()
    {
        using var step = new DomainJoinStepViewModel();
        step.Configure(new() { IsEnabled = true });
        step.DomainName = "corp.test";

        using DomainJoinSubmission submission = step.CreateSubmission()!;

        Assert.Null(submission.SelectedDomainId);
        Assert.Equal("corp.test", submission.DomainName);
    }

    [Fact]
    public void ZeroTouchSubmissionNeverCarriesAnAccountOrPassword()
    {
        using var step = new DomainJoinStepViewModel();
        step.Configure(OneDomain(DomainJoinMode.Automatic));
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

        Assert.Equal("corp.test", step.DomainName);
        Assert.Equal(string.Empty, step.AccountName);
        Assert.False(step.HasPassword);
        Assert.Equal(1, cleared);
        Assert.Null(step.CreateSubmission());
    }

    [Fact]
    public void ReconfiguringWithListedDomainsShowsTheDefaultDomain()
    {
        using var step = new DomainJoinStepViewModel();
        step.Configure(new() { IsEnabled = true });
        step.DomainName = "typed.test";

        step.Configure(TwoDomains(DomainJoinMode.Interactive) with { DefaultDomainId = "emea" });

        Assert.Equal("emea.test", step.DomainName);
        Assert.Equal("emea", step.SelectedDomain?.Id);
    }

    [Theory]
    [InlineData(Foundry.Deploy.Services.Runtime.DebugDomainJoinMode.Interactive)]
    [InlineData(Foundry.Deploy.Services.Runtime.DebugDomainJoinMode.ZeroTouch)]
    public void DebugScenariosReachTheDomainListAndBothKindsOfDomain(Foundry.Deploy.Services.Runtime.DebugDomainJoinMode mode)
    {
        using var step = new DomainJoinStepViewModel();
        step.Configure(Foundry.Deploy.Services.Runtime.DebugDomainJoinScenarios.Create(mode));

        Assert.True(step.HasInput);
        Assert.True(step.IsDomainListVisible);
        Assert.True(step.IsOuListVisible);

        step.SelectedDomain = step.Domains[1];

        Assert.False(step.IsOuListVisible);
        Assert.Null(step.EffectiveOuDistinguishedName);
    }

    private static DeployDomainJoinSettings TwoDomains(DomainJoinMode mode) => DomainJoinPreparationServiceTests.TwoDomains(mode, new byte[32]);

    private static DeployDomainJoinSettings OneDomain(DomainJoinMode mode)
    {
        DeployDomainJoinSettings settings = TwoDomains(mode);
        return settings with { Domains = [settings.Domains[0]] };
    }
}
