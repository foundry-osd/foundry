// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Configuration;
using Foundry.Core.Services.NetworkCache;
using Foundry.Core.Services.Sharing;
using Foundry.Deploy.Models.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using DeployNetworkCacheSettings = Foundry.Core.Models.Configuration.Deploy.DeployNetworkCacheSettings;

namespace Foundry.Deploy.Services.Cache;

/// <summary>Outcome of the network cache startup check.</summary>
/// <param name="Document">
/// The configuration the wizard and the deployment use: with the three sections of the verified index on success, otherwise
/// the configuration that was checked.
/// </param>
/// <param name="Failure">Why the deployment cannot start, or <see langword="null"/> when it can.</param>
public sealed record NetworkCacheStartupResult(FoundryDeployConfigurationDocument Document, NetworkCacheStartupFailure? Failure);

/// <summary>
/// Verifies, before the wizard opens, that the share of a network cache with a publisher key holds a trusted index and the
/// content it references, then applies the custom image, custom driver and post-installation sections of that index to the
/// configuration. A configuration without a publisher key keeps its own sections and the share is never read. With a key,
/// nothing falls back to the sections of the boot image: any problem is a startup failure.
/// </summary>
public sealed class NetworkCacheStartupCheck
{
    /// <summary>The longest the index read, then the content presence check, may each take before the share counts as unreachable.</summary>
    internal static readonly TimeSpan DefaultShareReadTimeout = TimeSpan.FromMinutes(2);

    private readonly ILogger _logger;
    private readonly TimeSpan _shareReadTimeout;
    private readonly Func<string, NetworkCacheContentIndex, CancellationToken, Task<IReadOnlyList<string>>> _findMissing;
    private readonly Func<string, string, CancellationToken, Task<byte[]?>> _readIndex;

    public NetworkCacheStartupCheck(ILogger<NetworkCacheStartupCheck>? logger = null)
        : this(logger, DefaultShareReadTimeout, NetworkCacheContentPresence.FindMissingAsync)
    {
    }

    /// <summary>Creates a check with its own share read timeout, presence check and index reader.</summary>
    internal NetworkCacheStartupCheck(ILogger<NetworkCacheStartupCheck>? logger, TimeSpan shareReadTimeout,
        Func<string, NetworkCacheContentIndex, CancellationToken, Task<IReadOnlyList<string>>> findMissing,
        Func<string, string, CancellationToken, Task<byte[]?>>? readIndex = null)
    {
        _logger = logger ?? NullLogger<NetworkCacheStartupCheck>.Instance;
        _shareReadTimeout = shareReadTimeout;
        _findMissing = findMissing;
        _readIndex = readIndex ?? NetworkCacheIndexStore.ReadAsync;
    }

    /// <summary>
    /// Runs the check off the calling thread, because share I/O can stall. The index read and the content presence check are
    /// each bounded by a timeout of two minutes, after which the share counts as unreachable, a cause Retry can resolve. A
    /// read the share refuses after the connection, or an unexpected error, is also reported with a cause Retry can resolve.
    /// </summary>
    /// <param name="document">The loaded Deploy configuration.</param>
    /// <param name="session">The session resolved for this deployment.</param>
    /// <param name="cancellationToken">Cancels the check.</param>
    public Task<NetworkCacheStartupResult> RunAsync(FoundryDeployConfigurationDocument document, NetworkCacheSession session, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(session);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.Run(() => RunCoreAsync(document, session, cancellationToken), cancellationToken);
    }

    private async Task<NetworkCacheStartupResult> RunCoreAsync(FoundryDeployConfigurationDocument document, NetworkCacheSession session,
        CancellationToken cancellationToken)
    {
        DeployNetworkCacheSettings settings = document.NetworkCache;
        if (!settings.IsEnabled || string.IsNullOrEmpty(settings.PublisherPublicKey))
        {
            _logger.LogDebug("Network cache content check skipped: the configuration carries no publisher key.");
            return new(document, null);
        }

        if (session.Failure is { } sessionFailure)
        {
            _logger.LogWarning("Network cache content check failed: the share cannot be used. FailureKind={FailureKind}, SharePath={SharePath}",
                sessionFailure.Kind, sessionFailure.SharePath);
            return new(document, sessionFailure);
        }

        if (!session.IsAvailable || session.SharePath is not { } sharePath || session.ContentRoot is not { } contentRoot)
        {
            // A key without a usable share is never a pass: the boot image no longer carries the content sections.
            _logger.LogWarning("Network cache content check failed: no share is connected. Outcome={Outcome}", session.Outcome);
            return new(document, new NetworkCacheStartupFailure(NetworkCacheStartupFailureKind.Unreachable, session.SharePath));
        }

        byte[] publicKey;
        try
        {
            publicKey = Convert.FromBase64String(settings.PublisherPublicKey);
        }
        catch (FormatException)
        {
            _logger.LogError("Network cache content check failed: the publisher key of the boot image is not base64. SharePath={SharePath}", sharePath);
            return new(document, new NetworkCacheStartupFailure(NetworkCacheStartupFailureKind.NotPublished, sharePath));
        }

        // The key id names the file; it is derived from the trusted key so a wrong configured id cannot select another file.
        string keyId = PublicationSigner.KeyId(publicKey);
        try
        {
            (bool indexRead, byte[]? file) = await ReadWithinTimeoutAsync(token => _readIndex(contentRoot, keyId, token), cancellationToken).ConfigureAwait(false);
            if (!indexRead)
            {
                _logger.LogWarning(
                    "Network cache content check failed: the index read did not finish in time. SharePath={SharePath}, KeyId={KeyId}, TimeoutSeconds={TimeoutSeconds}",
                    sharePath, keyId, _shareReadTimeout.TotalSeconds);
                return new(document, new NetworkCacheStartupFailure(NetworkCacheStartupFailureKind.Unreachable, sharePath));
            }

            NetworkCacheContentIndex? index = null;
            NetworkCacheIndexReadStatus status = file is null
                ? NetworkCacheIndexReadStatus.Missing
                : NetworkCacheContentIndexCodec.Read(file, publicKey, settings.MinimumIndexSequence, out index);
            if (status != NetworkCacheIndexReadStatus.Valid)
            {
                // The unverified sequence only explains a TooOld status in the log; it never drives a decision.
                long? sequence = file is null ? null : NetworkCacheContentIndexCodec.ReadUnverified(file)?.Sequence;
                _logger.LogWarning(
                    "Network cache content check failed: the index is not usable. IndexStatus={IndexStatus}, SharePath={SharePath}, KeyId={KeyId}, IndexSequence={IndexSequence}, MinimumSequence={MinimumSequence}",
                    status, sharePath, keyId, sequence, settings.MinimumIndexSequence);
                return new(document, new NetworkCacheStartupFailure(NetworkCacheStartupFailureKind.NotPublished, sharePath));
            }

            NetworkCacheContentIndex verified = index!;

            // The same rule the loader applies to the post-installation section of a boot image configuration.
            IReadOnlyList<PreOobeValidationIssue> issues = PreOobeConfigurationValidator.Validate(
                new PreOobeSettings { IsEnabled = verified.PreOobe.IsEnabled, Actions = verified.PreOobe.Actions });
            if (issues.Count > 0)
            {
                _logger.LogWarning(
                    "Network cache content check failed: the index post-installation section is invalid. SharePath={SharePath}, KeyId={KeyId}, Sequence={Sequence}, ValidationCode={ValidationCode}",
                    sharePath, keyId, verified.Sequence, issues[0].Code);
                return new(document, new NetworkCacheStartupFailure(NetworkCacheStartupFailureKind.NotPublished, sharePath));
            }

            (bool presenceChecked, IReadOnlyList<string>? missing) = await ReadWithinTimeoutAsync(
                token => _findMissing(contentRoot, verified, token), cancellationToken).ConfigureAwait(false);
            if (!presenceChecked || missing is null)
            {
                _logger.LogWarning(
                    "Network cache content check failed: the content presence check did not finish in time. SharePath={SharePath}, KeyId={KeyId}, TimeoutSeconds={TimeoutSeconds}",
                    sharePath, keyId, _shareReadTimeout.TotalSeconds);
                return new(document, new NetworkCacheStartupFailure(NetworkCacheStartupFailureKind.Unreachable, sharePath));
            }

            if (missing.Count > 0)
            {
                _logger.LogWarning(
                    "Network cache content check failed: published content is missing. SharePath={SharePath}, KeyId={KeyId}, Sequence={Sequence}, MissingCount={MissingCount}, MissingItems={MissingItems}",
                    sharePath, keyId, verified.Sequence, missing.Count, missing);
                return new(document, new NetworkCacheStartupFailure(NetworkCacheStartupFailureKind.ContentMissing, sharePath) { MissingItems = missing });
            }

            _logger.LogInformation(
                "Network cache content check passed. SharePath={SharePath}, KeyId={KeyId}, Sequence={Sequence}, MinimumSequence={MinimumSequence}, DriverSetCount={DriverSetCount}, ActionCount={ActionCount}",
                sharePath, keyId, verified.Sequence, settings.MinimumIndexSequence, verified.CustomDrivers.Sets.Count, verified.PreOobe.Actions.Count);
            return new(document with
            {
                CustomImages = verified.CustomImages,
                CustomDrivers = verified.CustomDrivers,
                PreOobe = verified.PreOobe
            }, null);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The session connected, so a refused read comes from the share or file permissions on the server, which an
            // administrator can fix while Deploy waits; a refused logon is reported by the session and is not retryable.
            bool denied = exception is UnauthorizedAccessException;
            _logger.LogWarning("Network cache content check failed: the share could not be read. SharePath={SharePath}, KeyId={KeyId}, Reason={Reason}",
                sharePath, keyId, exception.GetType().Name);
            return new(document, new NetworkCacheStartupFailure(
                denied ? NetworkCacheStartupFailureKind.AccessDenied : NetworkCacheStartupFailureKind.Unreachable,
                sharePath,
                denied ? FindAccountName(settings, sharePath) : null)
            {
                DeniedAfterConnection = denied
            });
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            // A damaged file on the share must still reach the blocking dialog, never fault the startup; republishing fixes it.
            _logger.LogWarning("Network cache content check failed unexpectedly. SharePath={SharePath}, KeyId={KeyId}, Reason={Reason}",
                sharePath, keyId, exception.GetType().Name);
            return new(document, new NetworkCacheStartupFailure(NetworkCacheStartupFailureKind.NotPublished, sharePath));
        }
    }

    /// <summary>
    /// Runs one share read for at most the share read timeout and reports whether it finished. A stalled SMB call can ignore
    /// cancellation, so the check also stops waiting; the abandoned read ends on its own.
    /// </summary>
    private async Task<(bool Completed, T? Result)> ReadWithinTimeoutAsync<T>(Func<CancellationToken, Task<T>> read, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_shareReadTimeout);
        try
        {
            return (true, await read(timeout.Token).WaitAsync(_shareReadTimeout, cancellationToken).ConfigureAwait(false));
        }
        catch (Exception exception) when ((exception is TimeoutException or OperationCanceledException) && !cancellationToken.IsCancellationRequested)
        {
            return (false, default);
        }
    }

    /// <summary>Returns the account configured for the connected share, so an access denied failure can name it.</summary>
    private static string? FindAccountName(DeployNetworkCacheSettings settings, string sharePath) =>
        settings.Shares.FirstOrDefault(share =>
            string.Equals(share.Path.TrimEnd('\\'), sharePath.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))?.AccountName;
}
