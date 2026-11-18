// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Globalization;
using Foundry.Deploy.Services.Deployment;
using Foundry.Deploy.ViewModels;
using Foundry.Wpf.Theme;

namespace Foundry.Deploy.Tests;

[Collection(nameof(WpfApplicationHostCollection))]
public sealed class MainWindowViewModelPreferencesTests
{
    [Fact]
    public Task ApplyInitialPreferences_AppliesTheConnectThemeAndCultureToTheMenus() => RunWithRestoredCultureAsync(() =>
    {
        var theme = new AutopilotInteractiveTestRuntime.FixedTheme(ThemeMode.System);
        using MainWindowViewModel viewModel = ProgressPageContractTests.CreateMainWindowViewModel(new UnusedExecution(), theme);

        viewModel.ApplyInitialPreferences("Dark", "fr-FR");

        Assert.Equal(ThemeMode.Dark, theme.CurrentTheme);
        Assert.Equal(ThemeMode.Dark, viewModel.CurrentTheme);
        Assert.Equal("fr-FR", viewModel.CurrentCulture.Name);
        Assert.Equal("fr-FR", Assert.Single(viewModel.SupportedCultures, static option => option.IsSelected).Code);
    });

    [Fact]
    public Task ApplyInitialPreferences_WithoutOrWithUnknownValues_KeepsTheDefaults() => RunWithRestoredCultureAsync(() =>
    {
        var theme = new AutopilotInteractiveTestRuntime.FixedTheme(ThemeMode.Light);
        using MainWindowViewModel viewModel = ProgressPageContractTests.CreateMainWindowViewModel(new UnusedExecution(), theme);
        string culture = viewModel.CurrentCulture.Name;

        viewModel.ApplyInitialPreferences(null, null);
        viewModel.ApplyInitialPreferences("Sepia", "xx-NOPE");

        Assert.Equal(ThemeMode.Light, viewModel.CurrentTheme);
        Assert.Equal(culture, viewModel.CurrentCulture.Name);
    });

    // The localization service changes the process-wide default cultures; the next test must not inherit them.
    private static Task RunWithRestoredCultureAsync(Action test) => WpfApplicationTestHost.RunAsync(() =>
    {
        CultureInfo culture = CultureInfo.CurrentCulture;
        CultureInfo uiCulture = CultureInfo.CurrentUICulture;
        CultureInfo? defaultCulture = CultureInfo.DefaultThreadCurrentCulture;
        CultureInfo? defaultUiCulture = CultureInfo.DefaultThreadCurrentUICulture;
        try
        {
            test();
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = uiCulture;
            CultureInfo.DefaultThreadCurrentCulture = defaultCulture;
            CultureInfo.DefaultThreadCurrentUICulture = defaultUiCulture;
        }

        return Task.CompletedTask;
    });

    private sealed class UnusedExecution : IDeploymentExecutionService
    {
        public Task<DeploymentExecutionRunResult> ExecuteAsync(DeploymentContext context, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<DeploymentExecutionRunResult> ExecuteAsync(DeploymentContext context,
            Foundry.Deploy.Services.DomainJoin.DomainJoinPreparedInput? domainJoinInput, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
