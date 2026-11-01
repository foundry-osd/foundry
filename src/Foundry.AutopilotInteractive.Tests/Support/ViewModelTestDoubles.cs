// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Resources;
using Foundry.AutopilotInteractive.Services;
using Foundry.AutopilotInteractive.Services.Device;
using Foundry.AutopilotInteractive.ViewModels;
using Foundry.Utilities.Globalization;
using Foundry.Wpf.Localization;
using Foundry.Wpf.Shell;
using Foundry.Wpf.Theme;

namespace Foundry.AutopilotInteractive.Tests.Support;

/// <summary>Runs every action inline on the calling thread, so view-model tests are deterministic.</summary>
internal sealed class ImmediateUiDispatcher : IUiDispatcher
{
    public void Run(Action action) => action();
}

/// <summary>Creates the production localization service over the real application resources.</summary>
internal static class TestLocalization
{
    public static ResourceLocalizationService Create() =>
        new(new ResourceManager("Foundry.AutopilotInteractive.Strings.Resources", typeof(App).Assembly));
}

/// <summary>
/// Answers the skip confirmation with <see cref="Result"/> and counts the prompts. With <see cref="KeepOpen"/>, the question
/// stays open until its token is cancelled, which answers "no" like the real dialog.
/// </summary>
internal sealed class FakeSkipConfirmation : ISkipConfirmation
{
    public bool Result { get; set; }

    public bool KeepOpen { get; set; }

    public int Calls { get; private set; }

    public bool WasCancelled { get; private set; }

    public bool? LastRegistrationInProgress { get; private set; }

    public Task<bool> ConfirmAsync(bool registrationInProgress, CancellationToken ct)
    {
        Calls++;
        LastRegistrationInProgress = registrationInProgress;
        if (!KeepOpen)
        {
            return Task.FromResult(Result);
        }

        TaskCompletionSource<bool> answer = new();
        ct.Register(() =>
        {
            WasCancelled = true;
            answer.TrySetResult(false);
        });
        return answer.Task;
    }
}

internal sealed class FakeDeviceInformationReader : IDeviceInformationReader
{
    public DeviceInformation Read() => new("Contoso", "Model 1", "SERIAL1234");
}

internal sealed class FakeThemeService : IThemeService
{
    public ThemeMode CurrentTheme { get; private set; } = ThemeMode.System;

    public void SetTheme(ThemeMode theme) => CurrentTheme = theme;
}

/// <summary>Keyboard switcher over French, Belgian French and Dutch layouts that records the layouts it applies.</summary>
internal sealed class FakeKeyboardLayoutSwitcher(string activeLayoutId = "0000040C") : IKeyboardLayoutSwitcher
{
    private static readonly IReadOnlyList<KeyboardLayout> Layouts = KeyboardLayoutCatalog.FromEntries(
    [
        new KeyboardLayoutRegistryEntry("0000040C", "KBDFR.DLL", "French (Legacy, AZERTY)", null),
        new KeyboardLayoutRegistryEntry("0000080C", "KBDBE.DLL", "Belgian French", null),
        new KeyboardLayoutRegistryEntry("00000413", "KBDNE.DLL", "Dutch", null)
    ]);

    private string activeLayoutId = activeLayoutId;

    public bool FailApply { get; init; }

    public List<string> AppliedLayoutIds { get; } = [];

    public IReadOnlyList<KeyboardLayout> GetLayouts() => Layouts;

    public string GetActiveLayoutId() => activeLayoutId;

    public KeyboardLayoutSwitchResult Apply(KeyboardLayout layout)
    {
        if (FailApply)
        {
            return new KeyboardLayoutSwitchResult(false, layout.Id, "Simulated failure.");
        }

        AppliedLayoutIds.Add(layout.Id);
        activeLayoutId = layout.Id;
        return new KeyboardLayoutSwitchResult(true, layout.Id, null);
    }

    public KeyboardLayoutSwitchResult? RestoreHandoff() => null;
}

internal sealed class FakeApplicationShellService : IApplicationShellService
{
    public void ShowAbout()
    {
    }

    public bool ConfirmWarning(string title, string message) => false;

    public void Shutdown()
    {
    }
}
