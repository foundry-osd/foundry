// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Security.Cryptography;
using Foundry.Core.Models.Configuration.Deploy;
using Foundry.Core.Services.Autopilot;
using Foundry.Core.Services.NetworkCache;
using Foundry.Deploy.Services.Configuration;
using Foundry.Deploy.Services.Security;
using Foundry.Utilities.Networking;
using Microsoft.Extensions.Logging;

namespace Foundry.Deploy.Services.Cache;

/// <summary>Chooses and connects the network cache share for one deployment.</summary>
public interface INetworkCacheSessionResolver
{
    /// <summary>
    /// Tries the shares of the device's most specific scope in order and returns the first one that connects and
    /// answers. Never throws for share problems: an unusable cache is an unavailable session.
    /// </summary>
    Task<NetworkCacheSession> ResolveAsync(string workspaceRootPath, string sessionId, CancellationToken cancellationToken);
}

public sealed class NetworkCacheSessionResolver : INetworkCacheSessionResolver
{
    private readonly IDeployConfigurationService configuration;
    private readonly ISmbConnector connector;
    private readonly INetworkAdapterSnapshotProvider adapters;
    private readonly IDeploymentSecretKeyProvider keys;
    private readonly INetworkCacheProbe probe;
    private readonly TimeSpan connectTimeout;
    private readonly TimeSpan probeTimeout;
    private readonly ILogger<NetworkCacheSessionResolver> logger;

    public NetworkCacheSessionResolver(
        IDeployConfigurationService configuration,
        ISmbConnector connector,
        INetworkAdapterSnapshotProvider adapters,
        IDeploymentSecretKeyProvider keys,
        INetworkCacheProbe probe,
        ILogger<NetworkCacheSessionResolver> logger)
        : this(configuration, connector, adapters, keys, probe, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(5), logger)
    {
    }

    internal NetworkCacheSessionResolver(
        IDeployConfigurationService configuration,
        ISmbConnector connector,
        INetworkAdapterSnapshotProvider adapters,
        IDeploymentSecretKeyProvider keys,
        INetworkCacheProbe probe,
        TimeSpan connectTimeout,
        TimeSpan probeTimeout,
        ILogger<NetworkCacheSessionResolver> logger)
    {
        this.configuration = configuration;
        this.connector = connector;
        this.adapters = adapters;
        this.keys = keys;
        this.probe = probe;
        this.connectTimeout = connectTimeout;
        this.probeTimeout = probeTimeout;
        this.logger = logger;
    }

    public async Task<NetworkCacheSession> ResolveAsync(string workspaceRootPath, string sessionId, CancellationToken cancellationToken)
    {
        DeployNetworkCacheSettings settings = configuration.LoadOptional().Document?.NetworkCache ?? new();
        if (!settings.IsEnabled)
        {
            return NetworkCacheSession.Unavailable(NetworkCacheOutcome.Disabled);
        }

        IReadOnlyList<DeployNetworkCacheShare> candidates = NetworkCacheShareSelector.Select(settings, ReadLocalAddresses());
        logger.LogInformation("Resolving network cache. CandidateShares={CandidateShares}", candidates.Count);
        if (candidates.Count == 0)
        {
            logger.LogInformation("Network cache not used. Outcome={Outcome}", NetworkCacheOutcome.NoMatchingScope);
            return NetworkCacheSession.Unavailable(NetworkCacheOutcome.NoMatchingScope);
        }

        foreach (DeployNetworkCacheShare share in candidates)
        {
            NetworkCacheSession? session = await TryShareAsync(share, workspaceRootPath, sessionId, cancellationToken).ConfigureAwait(false);
            if (session is not null)
            {
                logger.LogInformation("Network cache connected. SharePath={SharePath}, CanWrite={CanWrite}", session.SharePath, session.CanWrite);
                return session;
            }
        }

        logger.LogWarning("Network cache unavailable; deployment continues without it. Outcome={Outcome}", NetworkCacheOutcome.Unavailable);
        return NetworkCacheSession.Unavailable(NetworkCacheOutcome.Unavailable);
    }

    private async Task<NetworkCacheSession?> TryShareAsync(DeployNetworkCacheShare share, string workspaceRootPath, string sessionId, CancellationToken cancellationToken)
    {
        bool hasAccount = !string.IsNullOrWhiteSpace(share.AccountName) && share.EncryptedCredentials is not null;
        SmbConnectResult result;
        try
        {
            byte[]? key = hasAccount ? await keys.ReadAsync(workspaceRootPath, cancellationToken).ConfigureAwait(false) : null;
            result = await Task.Run(() => Connect(share, key), cancellationToken)
                .WaitAsync(connectTimeout, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is TimeoutException or CryptographicException or InvalidDataException or InvalidOperationException or IOException)
        {
            logger.LogWarning("Network cache share skipped. SharePath={SharePath}, Reason={Reason}", share.Path, exception.GetType().Name);
            return null;
        }

        if (result.Status != SmbConnectStatus.Connected)
        {
            logger.LogWarning("Network cache share connection failed. SharePath={SharePath}, Status={Status}, Win32Error={Win32Error}",
                share.Path, result.Status, result.Win32Error);
            return null;
        }

        string contentRoot = NetworkCacheLayout.ContentRoot(share.Path);
        if (!await ProbeAsync(token => probe.CanReadAsync(contentRoot, token), cancellationToken).ConfigureAwait(false))
        {
            logger.LogWarning("Network cache share did not answer. SharePath={SharePath}", share.Path);
            connector.Disconnect(share.Path);
            return null;
        }

        bool canWrite = hasAccount &&
                        await ProbeAsync(token => probe.CanWriteAsync(contentRoot, sessionId, token), cancellationToken).ConfigureAwait(false);
        return NetworkCacheSession.Connected(share.Path, canWrite, connector);
    }

    private SmbConnectResult Connect(DeployNetworkCacheShare share, byte[]? key)
    {
        if (key is null)
        {
            return connector.Connect(share.Path, null, []);
        }

        byte[]? plaintext = null;
        try
        {
            plaintext = MediaSecretEnvelopeProtector.DecryptBytes(share.EncryptedCredentials!, key, MediaSecretEnvelopeProtector.DeploymentKeyId);
            using NetworkShareCredentialPayload payload = NetworkShareCredentialPayloadCodec.Decode(plaintext, new(share.Path, share.AccountName!));
            return connector.Connect(share.Path, share.AccountName, payload.Password.Span);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            if (plaintext is not null) CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    private async Task<bool> ProbeAsync(Func<CancellationToken, Task<bool>> check, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(probeTimeout);
        try
        {
            return await check(timeout.Token).WaitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }

    private IEnumerable<IPAddress> ReadLocalAddresses() => adapters.GetAdapters()
        .Where(adapter => adapter.OperationalStatus == OperationalStatus.Up)
        .SelectMany(adapter => adapter.UnicastAddresses)
        .Select(text => IPAddress.TryParse(text, out IPAddress? address) ? address : null)
        .OfType<IPAddress>()
        .ToArray();
}
