// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.AutopilotInteractive.Services;
using Foundry.AutopilotInteractive.Services.Authentication;
using Foundry.AutopilotInteractive.Services.Registration;
using Foundry.AutopilotInteractive.Services.Simulation;
using Foundry.AutopilotInteractive.Tests.Support;
using Foundry.AutopilotInteractive.ViewModels;
using Foundry.Core.Models.Autopilot;
using Foundry.Core.Services.Autopilot;
using Foundry.Wpf.Localization;
using Microsoft.Extensions.Logging.Abstractions;

namespace Foundry.AutopilotInteractive.Tests.ViewModels;

public sealed class GroupTagViewModelTests
{
    private static readonly AuthenticatedSession Session = new("token", DateTimeOffset.MaxValue, "technician@contoso.com");
    private readonly ManualTimeProvider _clock = new();
    private readonly ResourceLocalizationService _localization = TestLocalization.Create();

    public GroupTagViewModelTests()
    {
        SynchronizationContext.SetSynchronizationContext(null);
    }

    [Fact]
    public async Task GroupTag_FailureEnablesRegisterAgain()
    {
        FakeRegistrationService registration = new(new RegistrationOutcome(false, RegistrationFailureCodes.NetworkUnavailable, null, null));
        using GroupTagViewModel viewModel = CreateViewModel();
        List<string> failures = [];
        viewModel.RegistrationFailed += (_, code) => failures.Add(code);
        await viewModel.LoadAsync(Services(registration, offerAssignedUser: false), Session, CancellationToken.None);

        await viewModel.RegisterCommand.ExecuteAsync(null);

        Assert.True(viewModel.RegisterCommand.CanExecute(null));
        Assert.False(viewModel.IsBusy);
        Assert.Equal(_localization.GetString(RegistrationFailureCodes.ResourceKey(RegistrationFailureCodes.NetworkUnavailable)), viewModel.FailureBanner);
        Assert.Equal([RegistrationFailureCodes.NetworkUnavailable], failures);
    }

    [Fact]
    public async Task GroupTag_RegisterDisabled_WhenAssignedUserInvalid()
    {
        using GroupTagViewModel viewModel = CreateViewModel();
        await viewModel.LoadAsync(Services(new FakeRegistrationService(), offerAssignedUser: true), Session, CancellationToken.None);
        Assert.True(viewModel.ShowAssignedUser);

        viewModel.AssignedUser = "first last@contoso.com";
        Assert.False(viewModel.RegisterCommand.CanExecute(null));
        Assert.False(string.IsNullOrEmpty(viewModel.AssignedUserError));

        viewModel.AssignedUser = "user@contoso.com";
        Assert.True(viewModel.RegisterCommand.CanExecute(null));
        Assert.True(string.IsNullOrEmpty(viewModel.AssignedUserError));
    }

    private GroupTagViewModel CreateViewModel() =>
        new(_localization, new AutopilotInteractiveRuntimeConfiguration(), _clock, new ImmediateUiDispatcher(), NullLogger<GroupTagViewModel>.Instance);

    private AutopilotServiceSet Services(IRegistrationService registration, bool offerAssignedUser) =>
        new(
            new SimulatedDeviceCodeClient(DebugScenario.SignInCode, _clock),
            registration,
            new SimulatedProfileAssignment(DebugScenario.ProfileAssigned, (duration, ct) => Task.Delay(duration, _clock, ct), _clock.GetUtcNow),
            offerAssignedUser,
            null);

    private sealed class FakeRegistrationService(params RegistrationOutcome[] outcomes) : IRegistrationService
    {
        private readonly Queue<RegistrationOutcome> _outcomes = new(outcomes);

        public Task<RegistrationContext> LoadAsync(string accessToken, CancellationToken ct) =>
            Task.FromResult(new RegistrationContext(["Kiosk"], false, new AutopilotDeviceLookup(AutopilotDeviceLookupStatus.NotFound, null)));

        public Task<RegistrationOutcome> RegisterAsync(
            RegistrationRequest request,
            IProgress<AutopilotHardwareHashUploadProgress> progress,
            CancellationToken ct) => Task.FromResult(_outcomes.Dequeue());
    }
}
