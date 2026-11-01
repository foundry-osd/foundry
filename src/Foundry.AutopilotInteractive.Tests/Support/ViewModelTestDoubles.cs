// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Resources;
using Foundry.AutopilotInteractive.Services;
using Foundry.AutopilotInteractive.Services.Device;
using Foundry.AutopilotInteractive.ViewModels;
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

/// <summary>Answers the skip confirmation with <see cref="Result"/> and counts the prompts.</summary>
internal sealed class FakeSkipConfirmation : ISkipConfirmation
{
    public bool Result { get; set; }

    public int Calls { get; private set; }

    public Task<bool> ConfirmAsync()
    {
        Calls++;
        return Task.FromResult(Result);
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
