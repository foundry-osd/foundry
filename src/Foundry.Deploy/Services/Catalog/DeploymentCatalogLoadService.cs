// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO;
using System.Net.Http;
using System.Xml;
using Foundry.Core.Services.Catalog;
using Foundry.Deploy.Models;
using Foundry.Deploy.Services.Networking;

namespace Foundry.Deploy.Services.Catalog;

public sealed class DeploymentCatalogLoadService : IDeploymentCatalogLoadService
{
    private readonly Func<string, CancellationToken, Task<VerifiedCatalogDocument>> acquire;
    private readonly CatalogSnapshotStore? store;
    private readonly DeploymentNetworkPolicy policy;

    public DeploymentCatalogLoadService(VerifiedCatalogSnapshotAcquirer acquirer, CatalogSnapshotStore? store = null, DeploymentNetworkPolicy? policy = null)
        : this((id, token) => acquirer.AcquireAsync(id, VerifiedCatalogSources.GetUri(id), token), store, policy) { }
    internal DeploymentCatalogLoadService(Func<string, CancellationToken, Task<VerifiedCatalogDocument>> acquire, CatalogSnapshotStore? store = null, DeploymentNetworkPolicy? policy = null)
    {
        this.acquire = acquire;
        this.store = store;
        this.policy = policy ?? new(false);
    }

    public Task<DeploymentCatalogSnapshot> LoadAsync() => LoadAsync(new(policy.OfflineOnly, false));

    public async Task<DeploymentCatalogSnapshot> LoadAsync(CatalogLoadRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        bool offline = request.OfflineOnly || policy.OfflineOnly;
        var osTask = LoadOneAsync(VerifiedCatalogSources.OperatingSystems, OperatingSystemCatalogService.ParseVerified, offline, cancellationToken);
        var driversTask = LoadOneAsync(VerifiedCatalogSources.DriverPacks, DriverPackCatalogService.ParseVerified, offline, cancellationToken);
        await Task.WhenAll(osTask, driversTask).ConfigureAwait(false);
        var os = await osTask.ConfigureAwait(false);
        var drivers = await driversTask.ConfigureAwait(false);
        return new(os.Snapshot?.Items ?? [], drivers.Snapshot?.Items ?? [])
        {
            OperatingSystemSnapshot = os.Snapshot,
            DriverPackSnapshot = drivers.Snapshot,
            OperatingSystemFailure = os.Failure,
            DriverPackFailure = drivers.Failure,
            CanContinue = os.Snapshot is not null && (!request.RequireDriverPacks || drivers.Snapshot is not null)
        };
    }

    private async Task<(CatalogSnapshot<T>? Snapshot, string? Failure)> LoadOneAsync<T>(string id,
        Func<VerifiedCatalogDocument, IReadOnlyList<T>> parse, bool offline, CancellationToken token)
    {
        try
        {
            VerifiedCatalogDocument document = offline
                ? await (store ?? throw new InvalidDataException("No trusted catalog snapshot is available.")).LoadAsync(id, token).ConfigureAwait(false)
                : await acquire(id, token).ConfigureAwait(false);
            document = document with { Content = document.Content.ToArray() };
            IReadOnlyList<T> items = parse(document);
            string? warning = null;
            if (!offline && store is not null)
            {
                try { store.Publish(document); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                { warning = "The live catalog is available, but its candidate snapshot could not be saved."; }
            }
            return (new(items, document.Revision, document.SourceUri, document.RetrievedUtc, offline), warning);
        }
        catch (Exception error) when (error is InvalidDataException or IOException or UnauthorizedAccessException or HttpRequestException or XmlException or ArgumentException or InvalidOperationException or TimeoutException)
        {
            token.ThrowIfCancellationRequested();
            return (null, offline ? "The catalog pinned by this boot image is missing, incompatible, or invalid." : "The catalog source is unavailable or incompatible.");
        }
    }
}
