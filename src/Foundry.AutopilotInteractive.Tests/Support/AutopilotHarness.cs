// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO;
using System.Text.Json;
using Foundry.AutopilotInteractive.Services.Authentication;
using Foundry.AutopilotInteractive.Services.Registration;
using Foundry.AutopilotInteractive.Services.Simulation;
using Foundry.AutopilotInteractive.Startup;
using Foundry.AutopilotInteractive.ViewModels;
using Foundry.Core.Models.Autopilot;
using Foundry.Wpf.Localization;
using Microsoft.Extensions.Logging.Abstractions;

namespace Foundry.AutopilotInteractive.Tests.Support;

/// <summary>
/// The whole window state machine over the simulated services, a manual clock and an inline dispatcher. Every simulated
/// wait completes inside <see cref="ManualTimeProvider.Advance"/>, so a test drives the run step by step.
/// </summary>
internal sealed class AutopilotHarness : IDisposable
{
    private readonly string _stateDirectory = Path.Combine(Path.GetTempPath(), "foundry-autopilot-vm-" + Guid.NewGuid().ToString("N"));

    public AutopilotHarness(
        DateTimeOffset? deadlineUtc = null,
        bool offerAssignedUser = false,
        string? keyboardLayoutId = null,
        FakeKeyboardLayoutSwitcher? keyboard = null)
    {
        // Continuations then run inline inside Advance instead of being posted to a test synchronization context.
        SynchronizationContext.SetSynchronizationContext(null);

        Localization = TestLocalization.Create();
        AutopilotInteractiveRuntimeConfiguration configuration = new() { OfferAssignedUser = offerAssignedUser, KeyboardLayoutId = keyboardLayoutId };
        Keyboard = keyboard ?? new FakeKeyboardLayoutSwitcher();
        AutopilotInteractiveCommandLine commandLine = new(null, deadlineUtc);
        ImmediateUiDispatcher dispatcher = new();

        ViewModel = new MainWindowViewModel(
            Localization,
            new FakeThemeService(),
            new FakeApplicationShellService(),
            new FakeDeviceInformationReader(),
            configuration,
            Keyboard,
            new SignInViewModel(Localization, Clock, dispatcher, NullLogger<SignInFlow>.Instance),
            new GroupTagViewModel(Localization, configuration, Clock, dispatcher, NullLogger<GroupTagViewModel>.Instance),
            new SimulatedAutopilotServiceSetFactory(Clock, configuration, commandLine),
            new RegistrationResultStore(_stateDirectory),
            Confirmation,
            Clock,
            dispatcher,
            NullLogger<MainWindowViewModel>.Instance,
            isDebugSafeMode: true);
        ViewModel.ExitRequested += (_, _) => ExitRequests++;
    }

    public ManualTimeProvider Clock { get; } = new();

    public ResourceLocalizationService Localization { get; }

    public FakeSkipConfirmation Confirmation { get; } = new();

    public FakeKeyboardLayoutSwitcher Keyboard { get; }

    public MainWindowViewModel ViewModel { get; }

    public int ExitRequests { get; private set; }

    public string Text(string key) => Localization.GetString(key);

    public void Start(DebugScenario scenario) => ViewModel.SetDebugScenarioCommand.Execute(scenario);

    /// <summary>Completes the simulated sign-in and the page loading.</summary>
    public void ReachGroupTagPage()
    {
        Clock.Advance(SimulatedDeviceCodeClient.PollInterval);
        Clock.Advance(SimulatedRegistrationService.LoadDuration);
        Assert.Equal(AutopilotPage.GroupTag, ViewModel.CurrentPage);
    }

    /// <summary>Clicks Register and lets the three simulated upload steps finish.</summary>
    public Task RegisterAndUpload()
    {
        Task register = ViewModel.GroupTag.RegisterCommand.ExecuteAsync(null);
        Clock.Advance(3 * SimulatedRegistrationService.StepDuration);
        return register;
    }

    public bool HasResult => File.Exists(Path.Combine(_stateDirectory, AutopilotInteractiveLayout.ResultFileName));

    public JsonElement ReadResult()
    {
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(Path.Combine(_stateDirectory, AutopilotInteractiveLayout.ResultFileName)));
        return document.RootElement.Clone();
    }

    public void Dispose()
    {
        ViewModel.Dispose();
        if (Directory.Exists(_stateDirectory))
        {
            Directory.Delete(_stateDirectory, recursive: true);
        }
    }
}
