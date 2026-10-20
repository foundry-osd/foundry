// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;
using Foundry.Core.Models.Configuration.Deploy;
using Foundry.Core.Services.DomainJoin;
using Foundry.Deploy.Services.DomainJoin;
using Foundry.Deploy.ViewModels;

namespace Foundry.Deploy.Tests;

public sealed class DomainJoinStepViewModelTests
{
    [Theory]
    [InlineData(false, DomainJoinMode.Interactive, 1, false)]
    [InlineData(true, DomainJoinMode.Interactive, 1, true)]
    [InlineData(true, DomainJoinMode.Automatic, 1, true)]
    [InlineData(true, DomainJoinMode.Automatic, 2, true)]
    public void StepIsShownWheneverTheMediaConfiguresTheJoin(bool enabled, DomainJoinMode mode, int ouCount, bool expected)
    {
        using var step = new DomainJoinStepViewModel();
        DeployDomainJoinSettings settings = OneDomain(mode) with { IsEnabled = enabled };
        step.Configure(ouCount == 2 ? DomainJoinPreparationServiceTests.WithTwoOus(settings) : settings);

        Assert.Equal(expected, step.HasInput);
        Assert.Equal(expected, step.IsDomainListVisible);
    }

    [Fact]
    public void ZeroTouchStepWithOneDomainStartsValidOnThatDomain()
    {
        using var step = new DomainJoinStepViewModel();
        step.Configure(OneDomain(DomainJoinMode.Automatic));

        Assert.True(step.HasInput);
        Assert.True(step.IsValid);
        Assert.False(step.IsJoinDeclined);
        Assert.Equal("corp.test", step.DomainName);
        Assert.Equal("OU=Sales,DC=corp,DC=test", step.EffectiveOuDistinguishedName);
    }

    [Fact]
    public void ZeroTouchStepForOneDomainWithoutOusUsesTheDefaultLocation()
    {
        using var step = new DomainJoinStepViewModel();
        DeployDomainJoinSettings settings = OneDomain(DomainJoinMode.Automatic);
        step.Configure(settings with { Domains = [settings.Domains[0] with { OrganizationalUnits = [], DefaultOuId = null }] });

        Assert.True(step.HasInput);
        Assert.True(step.IsValid);
        Assert.Null(step.EffectiveOuDistinguishedName);
    }

    [Theory]
    [InlineData(0, DomainJoinMode.Interactive)]
    [InlineData(1, DomainJoinMode.Interactive)]
    [InlineData(1, DomainJoinMode.Automatic)]
    [InlineData(2, DomainJoinMode.Interactive)]
    [InlineData(2, DomainJoinMode.Automatic)]
    public void NotJoiningIsOfferedLastAndIsNeverPreselected(int domainCount, DomainJoinMode mode)
    {
        using var step = new DomainJoinStepViewModel();
        step.Configure(Settings(domainCount, mode));

        Assert.Equal(Math.Max(domainCount, 1) + 1, step.DomainChoices.Count);
        Assert.Equal(DomainJoinChoiceKind.DoNotJoin, step.DomainChoices[^1].Kind);
        Assert.Single(step.DomainChoices, choice => choice.Kind == DomainJoinChoiceKind.DoNotJoin);
        Assert.All(step.DomainChoices, choice => Assert.False(string.IsNullOrWhiteSpace(choice.DisplayName)));
        Assert.Same(step.DomainChoices[0], step.SelectedDomainChoice);
        Assert.Equal(domainCount == 0 ? DomainJoinChoiceKind.TypedDomain : DomainJoinChoiceKind.ListedDomain, step.SelectedDomainChoice!.Kind);
        Assert.False(step.IsJoinDeclined);
    }

    [Fact]
    public void ListedDomainsAreOfferedByNameInMediaOrder()
    {
        using var step = new DomainJoinStepViewModel();
        step.Configure(TwoDomains(DomainJoinMode.Automatic) with { DefaultDomainId = "emea" });

        Assert.Equal(["corp.test", "emea.test"], step.DomainChoices.Take(2).Select(choice => choice.DisplayName));
        Assert.Equal("emea", step.SelectedDomainChoice?.Domain?.Id);

        step.SelectedDomainChoice = step.DomainChoices[0];

        Assert.Equal("corp", step.SelectedDomain?.Id);
        Assert.Equal("corp.test", step.DomainName);
    }

    [Theory]
    [InlineData(0, DomainJoinMode.Interactive)]
    [InlineData(1, DomainJoinMode.Interactive)]
    [InlineData(2, DomainJoinMode.Automatic)]
    public void NotJoiningHidesEveryInputErasesThePasswordAndIsValid(int domainCount, DomainJoinMode mode)
    {
        using var step = new DomainJoinStepViewModel();
        DeployDomainJoinSettings settings = Settings(domainCount, mode);
        step.Configure(domainCount == 0 ? settings : DomainJoinPreparationServiceTests.WithTwoOus(settings));
        step.SetPassword("secret");
        int cleared = 0;
        step.PasswordCleared += (_, _) => cleared++;

        step.SelectedDomainChoice = step.DomainChoices[^1];

        Assert.True(step.IsJoinDeclined);
        Assert.True(step.HasInput);
        Assert.True(step.IsDomainListVisible);
        Assert.False(step.IsDomainTextVisible);
        Assert.False(step.RequiresCredentials);
        Assert.False(step.IsOuListVisible);
        Assert.False(step.IsTypedOuVisible);
        Assert.False(step.IsDomainInvalid);
        Assert.False(step.HasPassword);
        Assert.Equal(1, cleared);
        Assert.Null(step.EffectiveOuDistinguishedName);
        Assert.True(step.IsValid);
        Assert.Same(step.DomainChoices[^1], step.SelectedDomainChoice);
    }

    [Fact]
    public void NotJoiningSubmitsNoDomainAccountPasswordOrOu()
    {
        using var step = new DomainJoinStepViewModel();
        step.Configure(DomainJoinPreparationServiceTests.WithTwoOus(TwoDomains(DomainJoinMode.Interactive)));
        step.AccountName = "CORP\\join";
        step.SetPassword("secret");

        step.SelectedDomainChoice = step.DomainChoices[^1];
        using DomainJoinSubmission submission = step.CreateSubmission()!;

        Assert.True(submission.DeclinesJoin);
        Assert.Null(submission.SelectedDomainId);
        Assert.Equal(string.Empty, submission.DomainName);
        Assert.Equal(string.Empty, submission.AccountName);
        Assert.Null(submission.SelectedOuId);
        Assert.Null(submission.TypedOuDistinguishedName);
        Assert.True(submission.Password.IsEmpty);
    }

    [Fact]
    public void ChoosingADomainAgainRestoresTheJoinAndItsInputs()
    {
        using var step = new DomainJoinStepViewModel();
        step.Configure(DomainJoinPreparationServiceTests.WithTwoOus(TwoDomains(DomainJoinMode.Interactive)));
        step.AccountName = "CORP\\join";
        step.SelectedDomainChoice = step.DomainChoices[^1];

        step.SelectedDomainChoice = step.DomainChoices[0];

        Assert.False(step.IsJoinDeclined);
        Assert.True(step.RequiresCredentials);
        Assert.True(step.IsOuListVisible);
        Assert.Equal("corp.test", step.DomainName);
        Assert.Equal("CORP\\join", step.AccountName);
        // The password was erased when the join was declined, so it must be typed again.
        Assert.False(step.IsValid);
        using DomainJoinSubmission submission = step.CreateSubmission()!;
        Assert.False(submission.DeclinesJoin);
        Assert.Equal("corp", submission.SelectedDomainId);
    }

    [Fact]
    public void ATypedDomainIsKeptWhileTheJoinIsDeclined()
    {
        using var step = new DomainJoinStepViewModel();
        step.Configure(new() { IsEnabled = true });
        step.DomainName = "corp.test";

        step.SelectedDomainChoice = step.DomainChoices[^1];
        Assert.False(step.IsDomainTextVisible);
        step.SelectedDomainChoice = step.DomainChoices[0];

        Assert.True(step.IsDomainTextVisible);
        Assert.Equal("corp.test", step.DomainName);
    }

    [Fact]
    public void ReconfiguringOffersTheJoinAgain()
    {
        using var step = new DomainJoinStepViewModel();
        step.Configure(OneDomain(DomainJoinMode.Automatic));
        step.SelectedDomainChoice = step.DomainChoices[^1];

        step.Configure(TwoDomains(DomainJoinMode.Automatic));

        Assert.False(step.IsJoinDeclined);
        Assert.Equal("corp", step.SelectedDomainChoice?.Domain?.Id);
    }

    [Fact]
    public void TheListControlClearingItsSelectionIsNotAChoice()
    {
        using var step = new DomainJoinStepViewModel();
        step.Configure(TwoDomains(DomainJoinMode.Automatic));

        step.SelectedDomainChoice = null;

        Assert.False(step.IsJoinDeclined);
        Assert.Equal("corp", step.SelectedDomainChoice?.Domain?.Id);
    }

    [Fact]
    public void NoChoiceIsOfferedWhenTheMediaDoesNotConfigureTheJoin()
    {
        using var step = new DomainJoinStepViewModel();
        step.Configure(new());

        Assert.Empty(step.DomainChoices);
        Assert.Null(step.SelectedDomainChoice);
        Assert.False(step.HasInput);
    }

    [Fact]
    public void ZeroTouchStepIsShownForADomainChoiceAlone()
    {
        using var step = new DomainJoinStepViewModel();
        step.Configure(TwoDomains(DomainJoinMode.Automatic));

        Assert.True(step.HasInput);
        Assert.True(step.IsDomainListVisible);
        Assert.False(step.IsDomainTextVisible);
        Assert.False(step.IsOuListVisible);
        Assert.True(step.IsValid);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void ListedDomainsAreShownInTheListAndNeverTyped(int domainCount)
    {
        using var step = new DomainJoinStepViewModel();
        step.Configure(Settings(domainCount, DomainJoinMode.Interactive));

        Assert.True(step.IsDomainListVisible);
        Assert.False(step.IsDomainTextVisible);
        Assert.Equal("corp.test", step.DomainName);
    }

    [Fact]
    public void ChangingTheDomainOffersItsOusAndPreselectsItsDefault()
    {
        using var step = new DomainJoinStepViewModel();
        DeployDomainJoinSettings settings = TwoDomains(DomainJoinMode.Interactive);
        settings = settings with
        {
            Domains =
            [
                settings.Domains[0],
                settings.Domains[1] with
                {
                    DefaultOuId = "kiosk",
                    OrganizationalUnits =
                    [
                        new() { Id = "desk", DisplayName = "Desks", DistinguishedName = "OU=Desks,DC=emea,DC=test" },
                        new() { Id = "kiosk", DisplayName = "Kiosks", DistinguishedName = "OU=Kiosks,DC=emea,DC=test" }
                    ]
                }
            ]
        };
        step.Configure(settings);
        Assert.Equal("sales", step.SelectedOrganizationalUnit?.Id);
        Assert.False(step.IsOuListVisible);

        step.SelectedDomain = step.Domains[1];

        Assert.True(step.IsOuListVisible);
        Assert.Equal("emea.test", step.DomainName);
        Assert.Equal("kiosk", step.SelectedOrganizationalUnit?.Id);
        Assert.All(step.OrganizationalUnits, unit => Assert.EndsWith("DC=emea,DC=test", unit.DistinguishedName));
        Assert.Equal("OU=Kiosks,DC=emea,DC=test", step.EffectiveOuDistinguishedName);
    }

    [Fact]
    public void ChangingTheDomainKeepsTheTypedAccountAndPassword()
    {
        using var step = new DomainJoinStepViewModel();
        step.Configure(TwoDomains(DomainJoinMode.Interactive));
        step.AccountName = "CORP\\join";
        step.SetPassword("secret");

        step.SelectedDomain = step.Domains[1];

        Assert.Equal("CORP\\join", step.AccountName);
        Assert.True(step.HasPassword);
    }

    [Theory]
    [InlineData(DomainJoinMode.Automatic)]
    [InlineData(DomainJoinMode.Interactive)]
    public void ADomainWithoutListedOusDoesNotBlock(DomainJoinMode mode)
    {
        using var step = new DomainJoinStepViewModel();
        step.Configure(TwoDomains(mode));
        step.AccountName = "EMEA\\tech";
        step.SetPassword("secret");

        step.SelectedDomain = step.Domains[1];

        Assert.False(step.IsOuListVisible);
        Assert.Equal(mode == DomainJoinMode.Interactive, step.IsTypedOuVisible);
        Assert.Null(step.EffectiveOuDistinguishedName);
        Assert.True(step.IsValid);
    }

    [Fact]
    public void InteractiveWithoutListedDomainsIsValidOnlyWithDomainQualifiedAccountAndPassword()
    {
        using var step = new DomainJoinStepViewModel();
        step.Configure(new() { IsEnabled = true });
        Assert.True(step.IsDomainTextVisible);
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
    public void SeveralListedOusRequireAChoiceAndPreselectTheDefault()
    {
        using var step = new DomainJoinStepViewModel();
        step.Configure(DomainJoinPreparationServiceTests.WithTwoOus(OneDomain(DomainJoinMode.Automatic)));

        Assert.True(step.IsOuListVisible);
        Assert.False(step.IsTypedOuVisible);
        Assert.Equal("OU=Sales,DC=corp,DC=test", step.EffectiveOuDistinguishedName);
        Assert.True(step.IsValid);

        step.SelectedOrganizationalUnit = null;
        Assert.False(step.IsValid);
    }

    [Fact]
    public void SeveralListedOusWithoutADefaultStartWithNoneSelected()
    {
        using var step = new DomainJoinStepViewModel();
        DeployDomainJoinSettings settings = DomainJoinPreparationServiceTests.WithTwoOus(OneDomain(DomainJoinMode.Automatic));
        step.Configure(settings with { Domains = [settings.Domains[0] with { DefaultOuId = null }] });

        Assert.True(step.HasInput);
        Assert.Null(step.SelectedOrganizationalUnit);
        Assert.False(step.IsValid);

        step.SelectedOrganizationalUnit = step.OrganizationalUnits[1];
        Assert.True(step.IsValid);
        Assert.Equal("OU=Field,DC=corp,DC=test", step.EffectiveOuDistinguishedName);
    }

    [Theory]
    [InlineData("sales")]
    [InlineData(null)]
    public void ASingleListedOuIsUsedWithoutAChoice(string? defaultOuId)
    {
        using var step = new DomainJoinStepViewModel();
        DeployDomainJoinSettings settings = OneDomain(DomainJoinMode.Interactive);
        step.Configure(settings with { Domains = [settings.Domains[0] with { DefaultOuId = defaultOuId }] });

        Assert.False(step.IsOuListVisible);
        Assert.False(step.IsTypedOuVisible);
        Assert.Equal("OU=Sales,DC=corp,DC=test", step.EffectiveOuDistinguishedName);
    }

    [Fact]
    public void SubmissionCarriesTheSelectedDomainTheInputsAndAnIndependentPasswordCopy()
    {
        using var step = new DomainJoinStepViewModel();
        step.Configure(TwoDomains(DomainJoinMode.Interactive));
        step.AccountName = "CORP\\join";
        step.SetPassword("secret");

        using DomainJoinSubmission first = step.CreateSubmission()!;
        step.SelectedDomain = step.Domains[1];
        using DomainJoinSubmission second = step.CreateSubmission()!;
        step.ClearPassword();

        Assert.Equal("corp", first.SelectedDomainId);
        Assert.Equal("corp.test", first.DomainName);
        Assert.Equal("CORP\\join", first.AccountName);
        // The only OU of corp.test is used as is, so nothing is submitted for it.
        Assert.Null(first.SelectedOuId);
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
        step.Configure(DomainJoinPreparationServiceTests.WithTwoOus(OneDomain(DomainJoinMode.Automatic)));
        step.AccountName = "CORP\\ignored";
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

    [Fact]
    public async Task NoCheckWhenCredentialsAreNotRequired()
    {
        var verifier = new ScriptedVerifier(DomainCredentialCheckOutcome.Valid);
        using var zeroTouch = new DomainJoinStepViewModel();
        zeroTouch.Configure(OneDomain(DomainJoinMode.Automatic));
        using var declined = InteractiveStepWithCredentials();
        declined.IsJoinDeclined = true;

        Assert.True(await zeroTouch.ConfirmCredentialsAsync(verifier, CancellationToken.None));
        Assert.True(await declined.ConfirmCredentialsAsync(verifier, CancellationToken.None));
        Assert.False(zeroTouch.NeedsCredentialCheck);
        Assert.False(declined.NeedsCredentialCheck);
        Assert.Equal(0, verifier.Calls);
    }

    [Fact]
    public async Task ValidCredentialsLetTheStepAdvance()
    {
        var verifier = new ScriptedVerifier(DomainCredentialCheckOutcome.Valid);
        using var step = InteractiveStepWithCredentials();

        Assert.True(step.NeedsCredentialCheck);
        Assert.True(await step.ConfirmCredentialsAsync(verifier, CancellationToken.None));
        Assert.Equal(DomainCredentialCheckState.Valid, step.CredentialCheckState);
        Assert.False(step.NeedsCredentialCheck);
        Assert.True(step.IsValid);
        Assert.Equal(string.Empty, step.CredentialCheckMessage);
        Assert.Equal(1, verifier.Calls);
    }

    [Fact]
    public async Task RejectionBlocksAndInvalidatesTheStep()
    {
        var verifier = new ScriptedVerifier(DomainCredentialCheckOutcome.InvalidCredentials);
        using var step = InteractiveStepWithCredentials();

        Assert.False(await step.ConfirmCredentialsAsync(verifier, CancellationToken.None));
        Assert.Equal(DomainCredentialCheckState.Rejected, step.CredentialCheckState);
        Assert.False(step.IsValid);
        Assert.True(step.HasCredentialCheckError);
        Assert.False(step.HasCredentialCheckWarning);
        Assert.NotEqual(string.Empty, step.CredentialCheckMessage);
        Assert.False(await step.ConfirmCredentialsAsync(verifier, CancellationToken.None));
        Assert.Equal(1, verifier.Calls);
    }

    [Theory]
    [InlineData("password")]
    [InlineData("account")]
    [InlineData("domainName")]
    [InlineData("selectedDomain")]
    public async Task ChangingAnInputAfterACheckRequiresANewCheck(string input)
    {
        var verifier = new ScriptedVerifier(DomainCredentialCheckOutcome.Valid, DomainCredentialCheckOutcome.InvalidCredentials);
        using var step = new DomainJoinStepViewModel();
        step.Configure(DomainJoinPreparationServiceTests.WithTwoOus(TwoDomains(DomainJoinMode.Interactive)));
        step.AccountName = "CORP\\join";
        step.SetPassword("secret");

        Assert.True(await step.ConfirmCredentialsAsync(verifier, CancellationToken.None));
        step.SelectedOrganizationalUnit = step.OrganizationalUnits[1];
        Assert.Equal(DomainCredentialCheckState.Valid, step.CredentialCheckState);

        ChangeInput(step, input, "first");
        Assert.Equal(DomainCredentialCheckState.NotChecked, step.CredentialCheckState);
        Assert.True(step.NeedsCredentialCheck);
        Assert.False(await step.ConfirmCredentialsAsync(verifier, CancellationToken.None));
        Assert.Equal(DomainCredentialCheckState.Rejected, step.CredentialCheckState);

        ChangeInput(step, input, "second");
        Assert.Equal(DomainCredentialCheckState.NotChecked, step.CredentialCheckState);
        Assert.True(step.IsValid);
        Assert.Equal(2, verifier.Calls);
    }

    [Fact]
    public async Task WarningIsAcknowledgedByTheNextNext()
    {
        var verifier = new ScriptedVerifier(DomainCredentialCheckOutcome.ControllerUnavailable);
        using var step = InteractiveStepWithCredentials();

        Assert.False(await step.ConfirmCredentialsAsync(verifier, CancellationToken.None));
        Assert.Equal(DomainCredentialCheckState.Warning, step.CredentialCheckState);
        Assert.True(step.HasCredentialCheckWarning);
        Assert.True(step.IsValid);
        Assert.True(step.NeedsCredentialCheck);

        Assert.True(await step.ConfirmCredentialsAsync(verifier, CancellationToken.None));
        Assert.Equal(DomainCredentialCheckState.WarningAcknowledged, step.CredentialCheckState);
        Assert.False(step.NeedsCredentialCheck);
        Assert.Equal(1, verifier.Calls);
    }

    [Fact]
    public async Task UnexpectedVerifierFailureWarnsInsteadOfStoppingDeploy()
    {
        var verifier = new ScriptedVerifier(Task.FromException<DomainCredentialCheckResult>(new DllNotFoundException()));
        using var step = InteractiveStepWithCredentials();

        Assert.False(await step.ConfirmCredentialsAsync(verifier, CancellationToken.None));
        Assert.Equal(DomainCredentialCheckState.Warning, step.CredentialCheckState);
        Assert.Equal(DomainCredentialCheckOutcome.Inconclusive, step.LastCredentialCheck?.Outcome);
        Assert.True(await step.ConfirmCredentialsAsync(verifier, CancellationToken.None));
    }

    [Fact]
    public async Task ResultOfAnOutdatedCheckIsIgnored()
    {
        var pending = new TaskCompletionSource<DomainCredentialCheckResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var verifier = new ScriptedVerifier(pending.Task);
        using var step = InteractiveStepWithCredentials();

        Task<bool> confirmation = step.ConfirmCredentialsAsync(verifier, CancellationToken.None);
        step.SetPassword("changed");
        pending.SetResult(new(DomainCredentialCheckOutcome.Valid));

        Assert.False(await confirmation);
        Assert.Equal(DomainCredentialCheckState.NotChecked, step.CredentialCheckState);
        Assert.True(step.NeedsCredentialCheck);
    }

    [Fact]
    public async Task CheckingDisablesTheStep()
    {
        var pending = new TaskCompletionSource<DomainCredentialCheckResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var step = InteractiveStepWithCredentials();

        Task<bool> confirmation = step.ConfirmCredentialsAsync(new ScriptedVerifier(pending.Task), CancellationToken.None);

        Assert.True(step.IsCheckingCredentials);
        Assert.False(step.IsValid);
        pending.SetResult(new(DomainCredentialCheckOutcome.Valid));
        Assert.True(await confirmation);
        Assert.False(step.IsCheckingCredentials);
    }

    [Fact]
    public async Task VerifierReceivesTheTypedDomainAccountAndPassword()
    {
        var verifier = new ScriptedVerifier(DomainCredentialCheckOutcome.Valid);
        using var step = new DomainJoinStepViewModel();
        step.Configure(new() { IsEnabled = true });
        step.DomainName = "lab.test";
        step.AccountName = "joiner@lab.test";
        step.SetPassword("typed secret");

        await step.ConfirmCredentialsAsync(verifier, CancellationToken.None);

        Assert.Equal("lab.test", verifier.LastContext?.DomainName);
        Assert.Equal("joiner@lab.test", verifier.LastContext?.AccountName);
        Assert.Equal("typed secret", verifier.LastPassword);
    }

    private static DomainJoinStepViewModel InteractiveStepWithCredentials()
    {
        var step = new DomainJoinStepViewModel();
        step.Configure(OneDomain(DomainJoinMode.Interactive));
        step.AccountName = "CORP\\join";
        step.SetPassword("secret");
        return step;
    }

    private static void ChangeInput(DomainJoinStepViewModel step, string input, string round)
    {
        switch (input)
        {
            case "password": step.SetPassword("secret-" + round); break;
            case "account": step.AccountName = "CORP\\join-" + round; break;
            case "domainName": step.DomainName = round + ".test"; break;
            default: step.SelectedDomain = step.Domains[round == "first" ? 1 : 0]; break;
        }
    }

    /// <summary>Returns scripted results in order, repeating the last one, and records what it was asked to verify.</summary>
    private sealed class ScriptedVerifier : IDomainCredentialVerifier
    {
        private readonly Queue<Task<DomainCredentialCheckResult>> results;
        private Task<DomainCredentialCheckResult> last;

        public ScriptedVerifier(params DomainCredentialCheckOutcome[] outcomes)
            : this(outcomes.Select(outcome => Task.FromResult(new DomainCredentialCheckResult(outcome))).ToArray())
        {
        }

        public ScriptedVerifier(params Task<DomainCredentialCheckResult>[] scripted)
        {
            results = new(scripted);
            last = scripted[^1];
        }

        public int Calls { get; private set; }

        public DomainJoinCredentialContext? LastContext { get; private set; }

        public string? LastPassword { get; private set; }

        public Task<DomainCredentialCheckResult> VerifyAsync(DomainJoinCredentialContext context, ReadOnlyMemory<char> password, CancellationToken token)
        {
            Calls++;
            LastContext = context;
            LastPassword = new string(password.Span);
            if (results.Count > 0) last = results.Dequeue();
            return last;
        }
    }

    private static DeployDomainJoinSettings TwoDomains(DomainJoinMode mode) => DomainJoinPreparationServiceTests.TwoDomains(mode, new byte[32]);

    /// <summary>Returns a media configuration that lists no domain (Interactive only), one domain or two.</summary>
    private static DeployDomainJoinSettings Settings(int domainCount, DomainJoinMode mode) =>
        domainCount switch { 0 => new() { IsEnabled = true }, 1 => OneDomain(mode), _ => TwoDomains(mode) };

    private static DeployDomainJoinSettings OneDomain(DomainJoinMode mode)
    {
        DeployDomainJoinSettings settings = TwoDomains(mode);
        return settings with { Domains = [settings.Domains[0]] };
    }
}
