// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Net;
using Foundry.Core.Services.Networking;
using Foundry.Services.Localization;
using Foundry.Services.Networking;
using Foundry.Services.Settings;
using Microsoft.UI.Xaml.Controls;
using Serilog;

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
        if (!TryCreateCandidate(out ProxyAppSettings settings, out ProxyCredential? credential))
        {
            return;
        }

        try
        {
            proxyService.ApplyAndSave(settings, credential);
            SetStatus("Proxy.Status.SavedTitle", "Proxy.Status.SavedMessage", InfoBarSeverity.Success);
        }
        catch (ApplicationProxyValidationException ex)
        {
            ShowValidationFailure(ex);
        }
        catch (Exception ex)
        {
            Log.ForContext<ProxySettingViewModel>().Error(
                ex,
                "Failed to apply proxy settings. Method={Method}, AuthenticationMode={AuthenticationMode}",
                Method,
                AuthenticationMode);
            SetStatus("Proxy.Status.ApplyFailedTitle", "Proxy.Status.ApplyFailedMessage", InfoBarSeverity.Error);
        }
    }

    public async Task TestConnectionAsync()
    {
        if (!TryCreateCandidate(out ProxyAppSettings settings, out ProxyCredential? credential))
        {
            return;
        }

        IsBusy = true;
        IsStatusOpen = false;
        try
        {
            await proxyService.TestConnectionAsync(settings, credential);
            SetStatus("Proxy.Status.SuccessTitle", "Proxy.Status.SuccessMessage", InfoBarSeverity.Success);
        }
        catch (ApplicationProxyValidationException ex)
        {
            ShowValidationFailure(ex);
        }
        catch (Exception ex)
        {
            Log.ForContext<ProxySettingViewModel>().Error(
                ex,
                "Proxy connection test failed. Method={Method}, AuthenticationMode={AuthenticationMode}",
                Method,
                AuthenticationMode);
            SetStatus(
                "Proxy.Status.FailedTitle",
                ex is HttpRequestException { StatusCode: HttpStatusCode.ProxyAuthenticationRequired }
                    ? "Proxy.Status.AuthenticationFailedMessage"
                    : "Proxy.Status.FailedMessage",
                InfoBarSeverity.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool TryCreateCandidate(out ProxyAppSettings settings, out ProxyCredential? credential)
    {
        settings = new ProxyAppSettings
        {
            Method = Method,
            AuthenticationMode = AuthenticationMode,
            Address = Address,
            Port = Port,
            BypassLocalAddresses = BypassLocalAddresses,
            BypassList = BypassList
        };
        credential = null;
        if (Method != ProxyMethod.Manual || AuthenticationMode != ProxyAuthenticationMode.Explicit)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(Username) || string.IsNullOrEmpty(Password))
        {
            SetStatus("Proxy.Status.InvalidTitle", "Proxy.Validation.CredentialsRequired", InfoBarSeverity.Error);
            return false;
        }

        credential = new ProxyCredential(Username.Trim(), Domain.Trim(), Password);
        return true;
    }

    private void ShowValidationFailure(ApplicationProxyValidationException exception)
    {
        Log.ForContext<ProxySettingViewModel>().Debug(
            "Proxy settings were rejected. Reason={Reason}, Method={Method}, AuthenticationMode={AuthenticationMode}",
            exception.Reason,
            Method,
            AuthenticationMode);
        string messageKey = exception.Reason switch
        {
            ApplicationProxyValidationReason.AddressRequired => "Proxy.Validation.AddressRequired",
            ApplicationProxyValidationReason.InvalidPort => "Proxy.Validation.InvalidPort",
            _ => "Proxy.Validation.InvalidAddress"
        };
        SetStatus("Proxy.Status.InvalidTitle", messageKey, InfoBarSeverity.Error);
    }

    private void SetStatus(string titleKey, string messageKey, InfoBarSeverity severity)
    {
        StatusTitle = localizationService.GetString(titleKey);
        StatusMessage = localizationService.GetString(messageKey);
        StatusSeverity = severity;
        IsStatusOpen = true;
    }
}
