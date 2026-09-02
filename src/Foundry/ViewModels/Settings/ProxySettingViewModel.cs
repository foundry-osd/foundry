// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Services.Localization;
using Foundry.Services.Networking;
using Foundry.Services.Settings;
using Microsoft.UI.Xaml.Controls;

namespace Foundry.ViewModels;

public sealed partial class ProxySettingViewModel : ObservableObject
{
    private readonly IApplicationProxyService proxyService;
    private readonly IApplicationLocalizationService localizationService;

    public ProxySettingViewModel(
        IAppSettingsService appSettingsService,
        IApplicationProxyService proxyService,
        IApplicationLocalizationService localizationService)
    {
        this.proxyService = proxyService;
        this.localizationService = localizationService;
        ProxyAppSettings settings = appSettingsService.Current.Proxy;
        Method = settings.Method;
        AuthenticationMode = settings.AuthenticationMode;
        Address = settings.Address;
        Port = settings.Port;
        BypassLocalAddresses = settings.BypassLocalAddresses;
        BypassList = settings.BypassList;
        ProxyCredential? credential = proxyService.ReadCredential();
        Username = credential?.Username ?? string.Empty;
        Domain = credential?.Domain ?? string.Empty;
        Password = credential?.Password ?? string.Empty;
    }

    [ObservableProperty] public partial ProxyMethod Method { get; set; }
    [ObservableProperty] public partial ProxyAuthenticationMode AuthenticationMode { get; set; }
    [ObservableProperty] public partial string Address { get; set; }
    [ObservableProperty] public partial int Port { get; set; }
    [ObservableProperty] public partial bool BypassLocalAddresses { get; set; }
    [ObservableProperty] public partial string BypassList { get; set; }
    [ObservableProperty] public partial string Username { get; set; }
    [ObservableProperty] public partial string Domain { get; set; }
    [ObservableProperty] public partial string Password { get; set; }
    [ObservableProperty] public partial bool IsBusy { get; set; }
    [ObservableProperty] public partial bool IsStatusOpen { get; set; }
    [ObservableProperty] public partial string StatusTitle { get; set; } = string.Empty;
    [ObservableProperty] public partial string StatusMessage { get; set; } = string.Empty;
    [ObservableProperty] public partial InfoBarSeverity StatusSeverity { get; set; }

    public void Apply()
    {
        try
        {
            ApplySettings();
            SetStatus("Proxy.Status.SavedTitle", "Proxy.Status.SavedMessage", InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            SetStatus("Proxy.Status.InvalidTitle", ex.Message, InfoBarSeverity.Error, false);
        }
    }

    public async Task TestConnectionAsync()
    {
        IsBusy = true;
        IsStatusOpen = false;
        try
        {
            (ProxyAppSettings settings, ProxyCredential? credential) = CreateCandidate();
            await proxyService.TestConnectionAsync(settings, credential);
            SetStatus("Proxy.Status.SuccessTitle", "Proxy.Status.SuccessMessage", InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            SetStatus("Proxy.Status.FailedTitle", ex.Message, InfoBarSeverity.Error, false);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ApplySettings()
    {
        (ProxyAppSettings settings, ProxyCredential? credential) = CreateCandidate();
        proxyService.ApplyAndSave(settings, credential);
    }

    private (ProxyAppSettings Settings, ProxyCredential? Credential) CreateCandidate()
    {
        ProxyCredential? credential = null;
        if (Method == ProxyMethod.Manual && AuthenticationMode == ProxyAuthenticationMode.Explicit)
        {
            if (string.IsNullOrWhiteSpace(Username) || string.IsNullOrEmpty(Password))
            {
                throw new ArgumentException(localizationService.GetString("Proxy.Validation.CredentialsRequired"));
            }

            credential = new ProxyCredential(Username.Trim(), Domain.Trim(), Password);
        }

        return (new ProxyAppSettings
        {
            Method = Method,
            AuthenticationMode = AuthenticationMode,
            Address = Address,
            Port = Port,
            BypassLocalAddresses = BypassLocalAddresses,
            BypassList = BypassList
        }, credential);
    }

    private void SetStatus(string titleKey, string message, InfoBarSeverity severity, bool localizeMessage = true)
    {
        StatusTitle = localizationService.GetString(titleKey);
        StatusMessage = localizeMessage ? localizationService.GetString(message) : message;
        StatusSeverity = severity;
        IsStatusOpen = true;
    }
}
