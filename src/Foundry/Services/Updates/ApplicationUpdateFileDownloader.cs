// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Net.Http;
using Velopack.Sources;

namespace Foundry.Services.Updates;

/// <summary>Connects Velopack metadata requests to the owned check deadline and package transfers to application lifetime.</summary>
internal sealed class ApplicationUpdateFileDownloader(CancellationToken lifetimeToken, CancellationToken metadataToken) : HttpClientFileDownloader
{
    public override async Task<byte[]> DownloadBytes(string url, IDictionary<string, string>? headers = null, double timeout = 30)
    {
        using HttpClient client = CreateHttpClient(headers, Math.Min(timeout, 0.25));
        return await TryDownloadThenLowercase(requestUrl => client.GetByteArrayAsync(requestUrl, metadataToken), url).ConfigureAwait(false);
    }

    public override async Task<string> DownloadString(string url, IDictionary<string, string>? headers = null, double timeout = 30)
    {
        using HttpClient client = CreateHttpClient(headers, Math.Min(timeout, 0.25));
        return await TryDownloadThenLowercase(requestUrl => client.GetStringAsync(requestUrl, metadataToken), url).ConfigureAwait(false);
    }

    protected override async Task<T> TryDownloadThenLowercase<T>(Func<string, Task<T>> downloadFunc, string url)
    {
        try { return await downloadFunc(url).ConfigureAwait(false); }
        catch (OperationCanceledException) { throw; }
        catch
        {
            try { return await downloadFunc(url.ToLowerInvariant()).ConfigureAwait(false); }
            catch (OperationCanceledException) { throw; }
            catch { }
            throw;
        }
    }

    public override async Task DownloadFile(string url, string targetFile, Action<int> progress,
        IDictionary<string, string>? headers = null, double timeout = 30, CancellationToken cancelToken = default)
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetimeToken, cancelToken);
        await base.DownloadFile(url, targetFile, progress, headers, timeout, cancellation.Token).ConfigureAwait(false);
    }
}
