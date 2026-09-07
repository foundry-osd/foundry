// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Net;

namespace Foundry.Telemetry;

/// <summary>Enforces a generation's consent at the actual HTTP boundary; owns the inner handler, not the shared generation.</summary>
public sealed class ConsentHttpMessageHandler(TelemetryConsentGeneration generation, HttpMessageHandler innerHandler)
    : DelegatingHandler(innerHandler)
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (!generation.TryBeginSend(out CancellationToken revoked))
            return new(HttpStatusCode.NoContent) { RequestMessage = request };
        var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, revoked);
        deadline.CancelAfter(TimeSpan.FromSeconds(2));
        CancellationToken deadlineToken = deadline.Token;
        Task<HttpResponseMessage> pending = SendAndCompleteAsync(request, deadline);
        try { return await pending.WaitAsync(deadlineToken).ConfigureAwait(false); }
        catch
        {
            // Observe and dispose any late response while retaining admission until the actual transport ends.
            _ = DisposeLateResponseAsync(pending);
            throw;
        }
    }

    private async Task<HttpResponseMessage> SendAndCompleteAsync(HttpRequestMessage request, CancellationTokenSource deadline)
    {
        try { return await base.SendAsync(request, deadline.Token).ConfigureAwait(false); }
        finally
        {
            deadline.Dispose();
            generation.EndSend();
        }
    }

    private static async Task DisposeLateResponseAsync(Task<HttpResponseMessage> pending)
    {
        try { (await pending.ConfigureAwait(false)).Dispose(); }
        catch (Exception) { }
    }
}
