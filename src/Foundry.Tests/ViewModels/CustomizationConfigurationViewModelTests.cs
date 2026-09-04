// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;
using Foundry.Core.Models.Configuration.Deploy;
using Foundry.Core.Services.Application;
using Foundry.Core.Services.Configuration;
using Foundry.DependencyInjection;
using Foundry.Localization;
using Foundry.Services.Configuration;
using Foundry.Services.Localization;
using Foundry.Telemetry;
using Foundry.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;

namespace Foundry.Tests.ViewModels;

public sealed class CustomizationConfigurationViewModelTests
{
    [Fact]
    public void OobeAccountsNeedsAttentionVisibility_WhenOobeIsDisabled_IsCollapsed()
    {
        using var viewModel = CreateViewModel();

        Assert.Equal(Visibility.Collapsed, viewModel.OobeAccountsNeedsAttentionVisibility);
    }

    [Fact]
    public void OobeAccountsNeedsAttentionVisibility_WhenAdministratorPasswordIsMissing_IsVisibleAndClearsAfterSecretsMatch()
    {
        using var viewModel = CreateViewModel(new FoundryConfigurationDocument
        {
            Customization = new CustomizationSettings
            {
                Oobe = new OobeSettings
                {
                    IsEnabled = true,
                    EnableAdministratorAccount = true,
                    UseAdministratorPassword = true
                }
            }
        });

        Assert.Equal(Visibility.Visible, viewModel.OobeAccountsNeedsAttentionVisibility);

        viewModel.SetOobeAdministratorPassword("AdminPassword123!");
        viewModel.SetOobeAdministratorConfirmation("AdminPassword123!");

        Assert.Equal(Visibility.Collapsed, viewModel.OobeAccountsNeedsAttentionVisibility);
    }

    [Fact]
    public void OobeAccountsNeedsAttentionVisibility_WhenAutopilotBlocksAccountProvisioning_IsVisible()
    {
        using var viewModel = CreateViewModel(
            new FoundryConfigurationDocument
            {
                Customization = new CustomizationSettings
                {
                    Oobe = new OobeSettings
                    {
                        IsEnabled = true
                    }
                }
            },
            isAutopilotEnabled: true);

        Assert.Equal(Visibility.Visible, viewModel.OobeAccountsNeedsAttentionVisibility);
    }

    private static CustomizationConfigurationViewModel CreateViewModel(
        FoundryConfigurationDocument? current = null,
        bool isAutopilotEnabled = false)
    {
        ServiceProvider provider = new ServiceCollection()
            .AddFoundryApplicationServices()
            .BuildServiceProvider();

        return new CustomizationConfigurationViewModel(
            new TestFoundryConfigurationStateService(current ?? new FoundryConfigurationDocument(), isAutopilotEnabled),
            new TestLanguageRegistryService(),
            new TestLocalizationService(),
            new TestDialogService(),
            provider.GetRequiredService<IOobeAccountSecretStateService>(),
            new TestOobeAdditionalAccountDialogService());
    }

    private sealed class TestFoundryConfigurationStateService : IFoundryConfigurationStateService
    {
        public TestFoundryConfigurationStateService(FoundryConfigurationDocument current, bool isAutopilotEnabled)
        {
            Current = current;
            IsAutopilotEnabled = isAutopilotEnabled;
        }

        public event EventHandler? StateChanged;

        public FoundryConfigurationDocument Current { get; private set; }

        public NetworkMediaReadinessEvaluation NetworkMediaReadiness => throw new NotSupportedException();

        public bool IsNetworkConfigurationReady => throw new NotSupportedException();

        public bool IsDeployConfigurationReady => throw new NotSupportedException();

        public bool IsConnectProvisioningReady => throw new NotSupportedException();

        public bool AreRequiredSecretsReady => throw new NotSupportedException();

        public bool IsAutopilotEnabled { get; }

        public bool IsAutopilotConfigurationReady => throw new NotSupportedException();

        public AutopilotConfigurationValidationResult AutopilotConfigurationValidation => throw new NotSupportedException();

        public AutopilotProvisioningMode AutopilotProvisioningMode => throw new NotSupportedException();

        public string? SelectedAutopilotProfileDisplayName => throw new NotSupportedException();

        public string? SelectedAutopilotProfileFolderName => throw new NotSupportedException();

        public void UpdateGeneral(GeneralSettings settings)
        {
            Current = Current with { General = settings };
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        public void UpdateNetwork(NetworkSettings settings)
        {
            Current = Current with { Network = settings };
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        public void UpdateOperatingSystemSelection(OperatingSystemSelectionSettings settings)
        {
            Current = Current with { OperatingSystemSelection = settings };
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        public void UpdateLocalization(LocalizationSettings settings)
        {
            Current = Current with { Localization = settings };
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        public void UpdateCustomization(CustomizationSettings settings)
        {
            Current = Current with { Customization = settings };
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        public void UpdateAutopilot(AutopilotSettings settings)
        {
            Current = Current with { Autopilot = settings };
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        public void UpdateTelemetry(TelemetrySettings settings)
        {
            Current = Current with { Telemetry = settings };
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        public FoundryConnectProvisioningBundle GenerateConnectProvisioningBundle(string stagingDirectoryPath, TelemetrySettings? telemetryOverride = null)
        {
            throw new NotSupportedException();
        }

        public string GenerateDeployConfigurationJson(
            TelemetrySettings? telemetryOverride = null,
            byte[]? deploymentSecretsKey = null,
            DeployProtectionSettings? protectionSettings = null)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class TestLanguageRegistryService : ILanguageRegistryService
    {
        public IReadOnlyList<LanguageRegistryEntry> GetLanguages() => [];
    }

    private sealed class TestLocalizationService : IApplicationLocalizationService
    {
        public string CurrentLanguage => "en-US";

        public event EventHandler<ApplicationLanguageChangedEventArgs>? LanguageChanged
        {
            add { }
            remove { }
        }

        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task SetLanguageAsync(string languageCode, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public string GetString(string key) => key switch
        {
            "Common.NeedsAttention" => "Needs attention",
            _ => key
        };

        public string FormatString(string key, params object[] args)
        {
            return string.Format(GetString(key), args);
        }

        public IReadOnlyList<SupportedCultureOption> CreateSupportedLanguageOptions() => [];
    }

    private sealed class TestDialogService : IDialogService
    {
        public Task ShowMessageAsync(DialogRequest request) => Task.CompletedTask;

        public Task<bool> ConfirmAsync(ConfirmationDialogRequest request) => Task.FromResult(false);
    }

    private sealed class TestOobeAdditionalAccountDialogService : IOobeAdditionalAccountDialogService
    {
        public Task<OobeAdditionalAccountDialogResult?> ShowAsync(
            OobeAdditionalAccountSettings? account,
            IReadOnlyList<OobeAdditionalAccountSettings> existingAccounts,
            char[] initialPassword,
            char[] initialConfirmation)
        {
            throw new NotSupportedException();
        }
    }
}
