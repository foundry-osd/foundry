// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Net;
using Foundry.Core.Services.Networking;
using Foundry.Services.Settings;
using Serilog;

namespace Foundry.Services.Networking;

public interface IApplicationProxyService
{
    ProxyCredential? ReadCredential();

    void ApplyAndSave(ProxyAppSettings settings, ProxyCredential? credential);

    Task TestConnectionAsync(
        ProxyAppSettings settings,
        ProxyCredential? credential,
        CancellationToken cancellationToken = default);
}

internal sealed class ApplicationProxyService : IApplicationProxyService
{
    private static readonly Uri[] TestEndpoints =
    [
        new("https://github.com"),
        new("https://login.microsoftonline.com"),
        new("https://graph.microsoft.com")
    ];

    private readonly IAppSettingsService appSettingsService;
    private readonly IProxyCredentialStore credentialStore;
    private readonly IWebProxy systemProxy;
    private readonly MutableApplicationProxy proxy;
    private readonly ILogger logger;

    public ApplicationProxyService(
        IAppSettingsService appSettingsService,
        IProxyCredentialStore credentialStore,
        ILogger logger)
    {
        this.appSettingsService = appSettingsService;
        this.credentialStore = credentialStore;
        this.logger = logger.ForContext<ApplicationProxyService>();
        systemProxy = HttpClient.DefaultProxy;
        systemProxy.Credentials ??= CredentialCache.DefaultNetworkCredentials;
        proxy = new MutableApplicationProxy(systemProxy);
        HttpClient.DefaultProxy = proxy;
        ProxyCredential? credential = null;
        try
        {
            credential = credentialStore.Read();
        }
        catch (Exception ex)
        {
            logger.Warning(ex, "Stored proxy credentials could not be read. Continuing without explicit credentials.");
        }

        try
        {
            ProxyAppSettings persistedSettings = appSettingsService.Current.Proxy ??= new ProxyAppSettings();
            proxy.Update(CreateProxy(persistedSettings, credential));
        }
        catch (ArgumentException ex)
        {
            logger.Warning(ex, "Invalid persisted proxy settings were ignored. Falling back to Windows proxy settings.");
            appSettingsService.Current.Proxy = new ProxyAppSettings();
            appSettingsService.Save();
            DeleteStoredCredential();
        }
    }

    public ProxyCredential? ReadCredential()
    {
        try
        {
            return credentialStore.Read();
        }
        catch (Exception ex)
        {
            logger.Warning(ex, "Stored proxy credentials could not be read.");
            return null;
        }
    }

    public void ApplyAndSave(ProxyAppSettings settings, ProxyCredential? credential)
    {
        ArgumentNullException.ThrowIfNull(settings);
        IWebProxy activeProxy = CreateProxy(settings, credential);

        if (settings.Method == ProxyMethod.Manual &&
            settings.AuthenticationMode == ProxyAuthenticationMode.Explicit &&
            credential is not null)
        {
            credentialStore.Save(credential);
        }
        else
        {
            credentialStore.Delete();
        }

        Copy(settings, appSettingsService.Current.Proxy);
        appSettingsService.Save();
        proxy.Update(activeProxy);
        logger.Information(
            "Foundry OSD proxy settings updated. Method={Method}, AuthenticationMode={AuthenticationMode}",
            settings.Method,
            settings.AuthenticationMode);
    }

    public async Task TestConnectionAsync(
        ProxyAppSettings settings,
        ProxyCredential? credential,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        IWebProxy candidateProxy = CreateProxy(settings, credential);
        using var handler = new HttpClientHandler { Proxy = candidateProxy, UseProxy = true };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
        foreach (Uri endpoint in TestEndpoints)
        {
            using var request = new HttpRequestMessage(HttpMethod.Head, endpoint);
            using HttpResponseMessage response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if ((int)response.StatusCode == 407)
            {
                throw new HttpRequestException("The proxy rejected the configured authentication.");
            }
        }
    }

    private IWebProxy CreateProxy(ProxyAppSettings settings, ProxyCredential? credential)
    {
        if (!Enum.IsDefined(settings.Method))
        {
            throw new ArgumentException("Select a valid proxy method.", nameof(settings));
        }

        if (!Enum.IsDefined(settings.AuthenticationMode))
        {
            throw new ArgumentException("Select a valid proxy authentication method.", nameof(settings));
        }

        return settings.Method switch
        {
            ProxyMethod.System => systemProxy,
            ProxyMethod.Direct => ApplicationProxyFactory.CreateDirect(),
            ProxyMethod.Manual => ApplicationProxyFactory.CreateManual(
                settings.Address,
                settings.Port,
                settings.BypassLocalAddresses,
                settings.BypassList,
                CreateCredentials(settings.AuthenticationMode, credential)),
            _ => systemProxy
        };
    }

    private void DeleteStoredCredential()
    {
        try
        {
            credentialStore.Delete();
        }
        catch (Exception ex)
        {
            logger.Warning(ex, "Obsolete proxy credentials could not be deleted.");
        }
    }

    private static ICredentials? CreateCredentials(ProxyAuthenticationMode mode, ProxyCredential? credential)
    {
        return mode switch
        {
            ProxyAuthenticationMode.Automatic => CredentialCache.DefaultNetworkCredentials,
            ProxyAuthenticationMode.Explicit when credential is not null =>
                new NetworkCredential(credential.Username, credential.Password, credential.Domain),
            _ => null
        };
    }

    private static void Copy(ProxyAppSettings source, ProxyAppSettings destination)
    {
        destination.Method = source.Method;
        destination.AuthenticationMode = source.AuthenticationMode;
        destination.Address = source.Address?.Trim() ?? string.Empty;
        destination.Port = source.Port;
        destination.BypassLocalAddresses = source.BypassLocalAddresses;
        destination.BypassList = source.BypassList?.Trim() ?? string.Empty;
    }
}
