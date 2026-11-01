// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Text.Json;
using Foundry.AutopilotInteractive.Services.Registration;
using Foundry.AutopilotInteractive.Services.Simulation;
using Foundry.AutopilotInteractive.Tests.Support;
using Foundry.AutopilotInteractive.ViewModels;
using Foundry.Wpf.Controls;

namespace Foundry.AutopilotInteractive.Tests.ViewModels;

public sealed class MainWindowViewModelTests
{
    [Fact]
    public async Task GroupTag_SessionExpired_ReturnsToSignIn()
    {
        using AutopilotHarness harness = new();
        harness.Start(DebugScenario.SessionExpired);
        string firstCode = harness.ViewModel.SignIn.UserCode;
        harness.ReachGroupTagPage();

        Task register = harness.ViewModel.GroupTag.RegisterCommand.ExecuteAsync(null);
        harness.Clock.Advance(SimulatedRegistrationService.StepDuration);
        await register;

        Assert.Equal(AutopilotPage.SignIn, harness.ViewModel.CurrentPage);
        Assert.Equal(InfoBarSeverity.Informational, harness.ViewModel.SignIn.BannerSeverity);
        Assert.Equal(harness.Text("SignIn.Banner.SessionExpired"), harness.ViewModel.SignIn.BannerText);
        Assert.True(harness.ViewModel.SignIn.IsCodeVisible);
        Assert.NotEqual(firstCode, harness.ViewModel.SignIn.UserCode);
    }

    [Fact]
    public void GroupTag_UseAnotherAccount_ReturnsToSignIn()
    {
        using AutopilotHarness harness = new();
        harness.Start(DebugScenario.ProfileAssigned);
        string firstCode = harness.ViewModel.SignIn.UserCode;
        harness.ReachGroupTagPage();

        harness.ViewModel.GroupTag.UseAnotherAccountCommand.Execute(null);

        Assert.Equal(AutopilotPage.SignIn, harness.ViewModel.CurrentPage);
        Assert.Equal(harness.Text("SignIn.Banner.SignedOut"), harness.ViewModel.SignIn.BannerText);
        Assert.True(harness.ViewModel.SignIn.IsCodeVisible);
        Assert.NotEqual(firstCode, harness.ViewModel.SignIn.UserCode);
    }

    [Fact]
    public async Task Close_AfterConfirmation_WritesSkippedAndExitCode1()
    {
        using AutopilotHarness harness = new();
        harness.Start(DebugScenario.SignInCode);

        harness.Confirmation.Result = false;
        Assert.False(await harness.ViewModel.ConfirmCloseAsync());
        Assert.False(harness.HasResult);

        harness.Confirmation.Result = true;
        Assert.True(await harness.ViewModel.ConfirmCloseAsync());

        Assert.Equal(2, harness.Confirmation.Calls);
        Assert.False(harness.Confirmation.LastRegistrationInProgress);
        Assert.Equal(1, harness.ViewModel.ExitCode);
        Assert.Equal(RegistrationResult.StatusSkipped, harness.ReadResult().GetProperty("status").GetString());
    }

    [Fact]
    public async Task Close_DuringUpload_WarnsThatTheDeviceMayAppear_AndWritesSkippedWithExitCode1()
    {
        using AutopilotHarness harness = new();
        harness.Start(DebugScenario.ProfileAssigned);
        harness.ReachGroupTagPage();
        Task register = harness.ViewModel.GroupTag.RegisterCommand.ExecuteAsync(null);
        harness.Clock.Advance(SimulatedRegistrationService.StepDuration);
        Assert.True(harness.ViewModel.GroupTag.IsBusy);
        harness.Confirmation.Result = true;

        Assert.True(await harness.ViewModel.ConfirmCloseAsync());
        await register;

        Assert.True(harness.Confirmation.LastRegistrationInProgress);
        Assert.Equal(1, harness.ViewModel.ExitCode);
        Assert.Equal(RegistrationResult.StatusSkipped, harness.ReadResult().GetProperty("status").GetString());
    }

    [Fact]
    public async Task Close_DuringSuccessCountdown_ExitsZeroWithoutPrompt()
    {
        using AutopilotHarness harness = new();
        harness.Start(DebugScenario.ProfileAssigned);
        harness.ReachGroupTagPage();
        Task register = harness.RegisterAndUpload();
        harness.Clock.Advance(SimulatedProfileAssignment.WaitDuration);
        await register;
        Assert.True(harness.ViewModel.GroupTag.IsCompleted);

        Assert.True(await harness.ViewModel.ConfirmCloseAsync());

        Assert.Equal(0, harness.Confirmation.Calls);
        Assert.Equal(0, harness.ViewModel.ExitCode);
        JsonElement result = harness.ReadResult();
        Assert.Equal(RegistrationResult.StatusCompleted, result.GetProperty("status").GetString());
        Assert.Equal("assigned", result.GetProperty("profileAssignment").GetString());
    }

    [Fact]
    public async Task Close_DuringProfileWait_WritesCompletedSkippedAndExitsZeroWithoutPrompt()
    {
        using AutopilotHarness harness = new();
        harness.Start(DebugScenario.ProfileWait);
        harness.ReachGroupTagPage();
        Task register = harness.RegisterAndUpload();
        Assert.True(harness.ViewModel.GroupTag.IsProfileWaiting);

        Assert.True(await harness.ViewModel.ConfirmCloseAsync());
        await register;

        Assert.Equal(0, harness.Confirmation.Calls);
        Assert.Equal(0, harness.ViewModel.ExitCode);
        Assert.Equal(1, harness.ExitRequests);
        JsonElement result = harness.ReadResult();
        Assert.Equal(RegistrationResult.StatusCompleted, result.GetProperty("status").GetString());
        Assert.Equal("skipped", result.GetProperty("profileAssignment").GetString());
    }

    [Fact]
    public async Task Deadline_WhileSkipPromptIsOpen_ClosesThePromptAndExitsOnce()
    {
        DateTimeOffset stopAt = new ManualTimeProvider().GetUtcNow() + TimeSpan.FromSeconds(20);
        using AutopilotHarness harness = new(deadlineUtc: stopAt + DeadlinePolicy.Margin);
        harness.Start(DebugScenario.SignInCode);
        harness.Confirmation.KeepOpen = true;
        Task<bool> close = harness.ViewModel.ConfirmCloseAsync();

        harness.Clock.Advance(stopAt - harness.Clock.GetUtcNow());

        Assert.True(harness.Confirmation.WasCancelled);
        Assert.False(await close);
        Assert.True(harness.ViewModel.HasDeadlineMessage);
        Assert.Equal(RegistrationFailureCodes.DeadlineReached, harness.ReadResult().GetProperty("failureCode").GetString());

        harness.Clock.Advance(MainWindowViewModel.DeadlineMessageDuration);

        Assert.Equal(1, harness.ExitRequests);
        Assert.Equal(1, harness.ViewModel.ExitCode);
        Assert.Equal(RegistrationResult.StatusFailed, harness.ReadResult().GetProperty("status").GetString());
    }

    [Fact]
    public async Task Registration_CompletingWhileSkipPromptIsOpen_ClosesThePromptAndCountsDown()
    {
        using AutopilotHarness harness = new();
        harness.Start(DebugScenario.ProfileAssigned);
        harness.ReachGroupTagPage();
        Task register = harness.ViewModel.GroupTag.RegisterCommand.ExecuteAsync(null);
        harness.Confirmation.KeepOpen = true;
        Task<bool> close = harness.ViewModel.ConfirmCloseAsync();

        harness.Clock.Advance(3 * SimulatedRegistrationService.StepDuration);

        Assert.True(harness.Confirmation.WasCancelled);
        Assert.False(await close);

        harness.Clock.Advance(SimulatedProfileAssignment.WaitDuration);
        await register;
        harness.Clock.Advance(GroupTagViewModel.SuccessCountdown);

        Assert.Equal(1, harness.Confirmation.Calls);
        Assert.Equal(1, harness.ExitRequests);
        Assert.Equal(0, harness.ViewModel.ExitCode);
        Assert.Equal(RegistrationResult.StatusCompleted, harness.ReadResult().GetProperty("status").GetString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Success_ExitsZero_AfterCountdownOrContinueNow(bool continueNow)
    {
        using AutopilotHarness harness = new();
        harness.Start(DebugScenario.ProfileAssigned);
        harness.ReachGroupTagPage();
        Task register = harness.RegisterAndUpload();
        harness.Clock.Advance(SimulatedProfileAssignment.WaitDuration);
        await register;

        if (continueNow)
        {
            harness.ViewModel.GroupTag.ContinueNowCommand.Execute(null);
        }
        else
        {
            harness.Clock.Advance(GroupTagViewModel.SuccessCountdown - TimeSpan.FromSeconds(1));
            Assert.Equal(0, harness.ExitRequests);
            harness.Clock.Advance(TimeSpan.FromSeconds(1));
        }

        Assert.Equal(1, harness.ExitRequests);
        Assert.Equal(0, harness.ViewModel.ExitCode);
        Assert.Equal(RegistrationResult.StatusCompleted, harness.ReadResult().GetProperty("status").GetString());
    }

    [Fact]
    public async Task Deadline_ReachedDuringUpload_WritesDeadlineReachedAndExitCode1()
    {
        using AutopilotHarness harness = new();
        DateTimeOffset stopAt = harness.Clock.GetUtcNow() + SimulatedAutopilotServiceSetFactory.SimulatedTimeBeforeDeadline;
        harness.Start(DebugScenario.DeadlineReached);
        harness.ReachGroupTagPage();
        Task register = harness.ViewModel.GroupTag.RegisterCommand.ExecuteAsync(null);
        harness.Clock.Advance(SimulatedRegistrationService.StepDuration);
        Assert.True(harness.ViewModel.GroupTag.IsBusy);

        harness.Clock.Advance(stopAt - harness.Clock.GetUtcNow());
        await register;

        JsonElement result = harness.ReadResult();
        Assert.Equal(RegistrationResult.StatusFailed, result.GetProperty("status").GetString());
        Assert.Equal(RegistrationFailureCodes.DeadlineReached, result.GetProperty("failureCode").GetString());
        Assert.Equal(1, harness.ViewModel.ExitCode);
        Assert.Equal(harness.Text("Deadline.Reached"), harness.ViewModel.DeadlineMessage);
        Assert.Equal(0, harness.ExitRequests);

        harness.Clock.Advance(MainWindowViewModel.DeadlineMessageDuration);

        Assert.Equal(1, harness.ExitRequests);
        Assert.Equal(1, harness.ViewModel.ExitCode);
    }

    [Fact]
    public async Task Deadline_ReachedDuringProfileWait_ExitsZero()
    {
        DateTimeOffset stopAt = new ManualTimeProvider().GetUtcNow() + TimeSpan.FromSeconds(20);
        using AutopilotHarness harness = new(deadlineUtc: stopAt + DeadlinePolicy.Margin);
        harness.Start(DebugScenario.ProfileWait);
        harness.ReachGroupTagPage();
        Task register = harness.RegisterAndUpload();
        Assert.True(harness.ViewModel.GroupTag.IsProfileWaiting);

        harness.Clock.Advance(stopAt - harness.Clock.GetUtcNow());
        await register;

        Assert.Equal(1, harness.ExitRequests);
        Assert.Equal(0, harness.ViewModel.ExitCode);
        JsonElement result = harness.ReadResult();
        Assert.Equal(RegistrationResult.StatusCompleted, result.GetProperty("status").GetString());
        Assert.Equal("timedOut", result.GetProperty("profileAssignment").GetString());
    }
}
