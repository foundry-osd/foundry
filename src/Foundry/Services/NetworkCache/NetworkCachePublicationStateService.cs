// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.NetworkCache;
using Foundry.Services.Configuration;
using Foundry.Services.Settings;

namespace Foundry.Services.NetworkCache;

/// <summary>
/// Computes <see cref="NetworkCachePublicationState.Evaluate"/> from the current configuration, the publication records kept in
/// the app settings and the signing role and key, and re-evaluates when the configuration, the records, the role or the key change.
/// </summary>
internal sealed class NetworkCachePublicationStateService : INetworkCachePublicationStateService, IDisposable
{
    private readonly IFoundryConfigurationStateService configuration;
    private readonly IAppSettingsService appSettings;
    private readonly INetworkCacheSigningService signing;
    // Shares whose last check, when the page measured them, found the published index or content missing; kept for this session.
    private readonly HashSet<string> sharesMissingContent = new(StringComparer.Ordinal);
    private NetworkCachePublicationStatus status = NetworkCachePublicationStatus.UpToDate;
    private bool canPublishIndex;
    private bool canSignIndex;
    private bool hasTrustedKey;
    private bool isDisposed;

    public NetworkCachePublicationStateService(
        IFoundryConfigurationStateService configuration,
        IAppSettingsService appSettings,
        INetworkCacheSigningService signing)
    {
        this.configuration = configuration;
        this.appSettings = appSettings;
        this.signing = signing;
        configuration.StateChanged += OnUnderlyingChanged;
        signing.Changed += OnUnderlyingChanged;
        Evaluate();
    }

    /// <inheritdoc />
    public event EventHandler? Changed;

    /// <inheritdoc />
    public bool CanPublishIndex => canPublishIndex;

    /// <inheritdoc />
    public bool CanSignIndex => canSignIndex;

    /// <inheritdoc />
    public bool HasTrustedKey => hasTrustedKey;

    /// <inheritdoc />
    public NetworkCachePublicationLevel Level => status.Level;

    /// <inheritdoc />
    public IReadOnlyList<string> FailedSharePaths => status.FailedShares.Select(share => share.Path).ToArray();

    /// <inheritdoc />
    public IReadOnlyList<string> MissingContentSharePaths => status.MissingContentShares.Select(share => share.Path).ToArray();

    /// <inheritdoc />
    public NetworkCachePublicationLevel LevelOf(string shareId) => status.LevelOf(shareId);

    /// <inheritdoc />
    public NetworkCacheShareExpectation? GetExpectation(string shareId)
    {
        if (!canPublishIndex || signing.TrustedPublicKey is not { } publicKey) return null;
        IReadOnlyList<NetworkCachePublicationRecord> records = NetworkCachePublicationRecords.Valid(appSettings.Current.NetworkCache.Publications);
        if (NetworkCachePublicationState.SharesNeedingPublication(configuration.Current, records)
            .Any(share => string.Equals(share.Id, shareId, StringComparison.Ordinal))) return null;
        NetworkCachePublicationRecord? latest = records
            .Where(record => string.Equals(record.ShareId, shareId, StringComparison.Ordinal))
            .MaxBy(record => record.Sequence);
        return latest is null ? null : new NetworkCacheShareExpectation(publicKey, latest.Sequence);
    }

    /// <inheritdoc />
    public void ReportContentCheck(string shareId, bool isIntact)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(shareId);
        bool changed = isIntact ? sharesMissingContent.Remove(shareId) : sharesMissingContent.Add(shareId);
        if (changed) Evaluate();
    }

    /// <inheritdoc />
    public void Record(FoundryConfigurationDocument published, IReadOnlyList<NetworkCacheContentPublishResult> results)
    {
        ArgumentNullException.ThrowIfNull(published);
        ArgumentNullException.ThrowIfNull(results);
        NetworkCacheAppSettings section = appSettings.Current.NetworkCache;
        section.Publications = [.. NetworkCachePublicationRecords.Apply(
            NetworkCachePublicationRecords.Valid(section.Publications), results, NetworkCachePublicationState.Fingerprint(published), DateTimeOffset.UtcNow)];
        try
        {
            appSettings.Save();
        }
        catch (Exception)
        {
            // Save already logged the failure; the records stay in memory for this session.
        }

        // A share that just received the publication holds it again.
        foreach (NetworkCacheContentPublishResult result in results.Where(result => result.Status == NetworkCacheContentPublishStatus.Published))
            sharesMissingContent.Remove(result.Share.Id);
        Evaluate();
    }

    public void Dispose()
    {
        if (isDisposed) return;
        isDisposed = true;
        configuration.StateChanged -= OnUnderlyingChanged;
        signing.Changed -= OnUnderlyingChanged;
    }

    private void OnUnderlyingChanged(object? sender, EventArgs e) => Evaluate();

    private void Evaluate()
    {
        if (isDisposed) return;
        bool canPublish = signing.CanPublishIndex;
        bool canSign = signing.CanSignIndex;
        bool hasTrusted = signing.TrustedPublicKey is not null;
        NetworkCachePublicationStatus current = NetworkCachePublicationState.Evaluate(
            configuration.Current, NetworkCachePublicationRecords.Valid(appSettings.Current.NetworkCache.Publications), canPublish, sharesMissingContent);
        bool isSame = canPublish == canPublishIndex && canSign == canSignIndex && hasTrusted == hasTrustedKey && current.IsUpToDate == status.IsUpToDate &&
            current.FailedShares.Select(share => share.Path).SequenceEqual(status.FailedShares.Select(share => share.Path), StringComparer.Ordinal) &&
            current.MissingContentShares.Select(share => share.Path).SequenceEqual(status.MissingContentShares.Select(share => share.Path), StringComparer.Ordinal) &&
            current.DueShares.Select(share => share.Id).SequenceEqual(status.DueShares.Select(share => share.Id), StringComparer.Ordinal) &&
            current.NotPublishedShares.Select(share => share.Id).SequenceEqual(status.NotPublishedShares.Select(share => share.Id), StringComparer.Ordinal);
        status = current;
        canPublishIndex = canPublish;
        canSignIndex = canSign;
        hasTrustedKey = hasTrusted;
        if (!isSame) Changed?.Invoke(this, EventArgs.Empty);
    }
}
