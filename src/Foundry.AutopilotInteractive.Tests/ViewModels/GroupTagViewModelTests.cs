// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Net;
using Foundry.AutopilotInteractive.Services;
using Foundry.AutopilotInteractive.Services.Authentication;
using Foundry.AutopilotInteractive.Services.Registration;
using Foundry.AutopilotInteractive.Services.Simulation;
using Foundry.AutopilotInteractive.Tests.Authentication;
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
    public async Task StatusKeyForProgress_MapsEveryMessageTheSharedGraphClientReports()
    {
        // Drives the real client through an import, a visible device with another tag, the update and its confirmation, so a
        // renamed or new progress text in Core fails here instead of silently freezing the step shown to the technician.
        int deviceListCalls = 0;
        FakeHttpMessageHandler handler = new((request, _) => request.RequestUri!.AbsolutePath switch
        {
            string path when path.EndsWith("/import", StringComparison.Ordinal) => FakeHttpMessageHandler.Json(
                HttpStatusCode.OK,
                """{ "value": [ { "id": "imported-1", "state": { "deviceImportStatus": "complete" } } ] }"""),
            string path when path.EndsWith("/updateDeviceProperties", StringComparison.Ordinal) => new HttpResponseMessage(HttpStatusCode.NoContent),
            _ => FakeHttpMessageHandler.Json(
                HttpStatusCode.OK,
                Interlocked.Increment(ref deviceListCalls) == 1
                    ? """{ "value": [ { "id": "d1", "serialNumber": "SER1", "groupTag": "Sales" } ] }"""
                    : """{ "value": [ { "id": "d1", "serialNumber": "SER1", "groupTag": "Marketing" } ] }""")
        });
        AutopilotGraphImportClient client = new(
            new HttpClient(handler) { BaseAddress = new Uri("https://graph.microsoft.com/") },
            NullLogger<AutopilotGraphImportClient>.Instance,
            new AutopilotGraphImportClientOptions { RetryCount = 0, RetryDelay = TimeSpan.Zero, PollInterval = TimeSpan.Zero });
        List<string> messages = [];

        AutopilotHardwareHashUploadResult result = await client.ImportHardwareHashAsync(
            new AutopilotGraphImportRequest("token", "SER1", "aGFzaA==", "Marketing", null, "import-1"),
            new SyncProgress(report => messages.Add(report.Message)),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsCompleted);
        Dictionary<string, string?> mapping = messages.Distinct().ToDictionary(message => message, GroupTagViewModel.StatusKeyForProgress);
        Assert.Equal(
            new Dictionary<string, string?>
            {
                ["Uploading Autopilot hardware hash..."] = "Registration.Status.Uploading",
                ["Waiting for Autopilot device visibility..."] = "Registration.Status.WaitingForRegistration",
                ["Updating Autopilot device..."] = "Registration.Status.Assigning",
                ["Waiting for Autopilot device property update..."] = "Registration.Status.Assigning"
            },
            mapping);
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

    private sealed class SyncProgress(Action<AutopilotHardwareHashUploadProgress> report) : IProgress<AutopilotHardwareHashUploadProgress>
    {
        public void Report(AutopilotHardwareHashUploadProgress value) => report(value);
    }
}
