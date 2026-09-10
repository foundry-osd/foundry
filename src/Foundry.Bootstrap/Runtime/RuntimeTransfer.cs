// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Net;
using System.Text.Json;
using Foundry.Utilities.IO;

namespace Foundry.Bootstrap.Runtime;

/// <summary>Streams payloads with bounded deadlines and retries only transient GET failures.</summary>
internal sealed class RuntimeTransfer(HttpClient client, Action<RuntimeDownloadProgress>? progress)
{
    /// <summary>Reads bounded GitHub release metadata without retaining HTTP response resources.</summary>
    public Task<JsonDocument> ReadReleaseAsync(string url, CancellationToken cancellationToken) => RetryAsync(async token =>
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(45));
        using HttpResponseMessage response = await GetAsync(url, deadline.Token).ConfigureAwait(false);
        await using Stream stream = await response.Content.ReadAsStreamAsync(deadline.Token).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        byte[] chunk = new byte[8192];
        int read;
        while ((read = await stream.ReadAsync(chunk, deadline.Token).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > 2 * 1024 * 1024) throw new InvalidDataException("Release metadata exceeds the size limit.");
            buffer.Write(chunk, 0, read);
        }
        buffer.Position = 0;
        return await JsonDocument.ParseAsync(buffer, cancellationToken: deadline.Token).ConfigureAwait(false);
    }, cancellationToken);

    /// <summary>Copies local overrides or downloads HTTP payloads, restarting partial GET transfers on transient failure.</summary>
    public async Task SaveAsync(string source, string destination, string applicationName, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(source, UriKind.Absolute, out Uri? uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            await using FileStream input = File.OpenRead(source);
            await using FileStream output = File.Create(destination);
            await StreamCopy.CopyAsync(input, output, null, cancellationToken).ConfigureAwait(false);
            return;
        }
        await RetryAsync(async token =>
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
            deadline.CancelAfter(TimeSpan.FromMinutes(15));
            using HttpResponseMessage response = await GetAsync(source, deadline.Token).ConfigureAwait(false);
            await using Stream input = await response.Content.ReadAsStreamAsync(deadline.Token).ConfigureAwait(false);
            await using FileStream output = File.Create(destination);
            long? total = response.Content.Headers.ContentLength;
            long received = await StreamCopy.CopyAsync(input, output,
                bytes => progress?.Invoke(new RuntimeDownloadProgress(applicationName, bytes, total)), deadline.Token).ConfigureAwait(false);
            if (total.HasValue && received != total.Value) throw new HttpRequestException("Payload transfer ended before the declared content length.");
            return true;
        }, cancellationToken).ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> GetAsync(string url, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd("FoundryBootstrap/1.0");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        HttpResponseMessage response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false);
        try
        {
            response.EnsureSuccessStatusCode();
            return response;
        }
        catch
        {
            response.Dispose();
            throw;
        }
    }

    private static async Task<T> RetryAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken)
    {
        for (int attempt = 0; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try { return await action(cancellationToken).ConfigureAwait(false); }
            catch (Exception exception) when (attempt < 2 && !cancellationToken.IsCancellationRequested && IsTransient(exception))
            {
                await Task.Delay(TimeSpan.FromSeconds(2 * (attempt + 1)), cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static bool IsTransient(Exception exception)
    {
        if (exception is OperationCanceledException) return true;
        if (exception is HttpIOException { HttpRequestError: HttpRequestError.ResponseEnded or HttpRequestError.ConnectionError }) return true;
        if (exception is not HttpRequestException request) return false;
        if (request.StatusCode.HasValue)
            return request.StatusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests || (int)request.StatusCode >= 500;
        return request.HttpRequestError is HttpRequestError.Unknown or HttpRequestError.NameResolutionError or
            HttpRequestError.ConnectionError or HttpRequestError.ResponseEnded;
    }
}
