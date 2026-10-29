// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Connect.Network.Policy;

/// <summary>
/// One connectivity-test endpoint and the exact body it must return.
/// </summary>
/// <param name="Uri">The plain HTTP address; HTTPS would hide a portal behind a certificate error.</param>
/// <param name="ExpectedBody">The exact response body, compared ordinally with no trimming.</param>
public sealed record NcsiEndpoint(Uri Uri, string ExpectedBody);

/// <summary>
/// What one endpoint returned during a probe.
/// </summary>
/// <param name="Endpoint">The probed endpoint.</param>
/// <param name="Responded">Whether any HTTP response was received; <see langword="false"/> on timeout or transport failure.</param>
/// <param name="StatusCode">The HTTP status code, or <see langword="null"/> when nothing responded.</param>
/// <param name="Body">The first bytes of the body decoded as UTF-8, or <see langword="null"/> when nothing responded.</param>
public sealed record ProbeObservation(NcsiEndpoint Endpoint, bool Responded, int? StatusCode, string? Body);

/// <summary>
/// Decides the internet status from the endpoint results of one probe (D2, L2-8).
/// </summary>
/// <remarks>
/// The configured <c>internetProbe</c> section is deliberately not used (D12). Only an exact
/// <c>200</c> answer with the expected body proves the internet; every other answer, including a redirect,
/// a proxy authentication request or an HTML sign-in page, means something intercepts web traffic.
/// </remarks>
public static class ConnectivityEvaluator
{
    /// <summary>Gets the two internal connectivity-test endpoints, probed in this order.</summary>
    public static IReadOnlyList<NcsiEndpoint> Endpoints { get; } =
    [
        new NcsiEndpoint(new Uri("http://www.msftconnecttest.com/connecttest.txt"), "Microsoft Connect Test"),
        new NcsiEndpoint(new Uri("http://www.msftncsi.com/ncsi.txt"), "Microsoft NCSI")
    ];

    /// <summary>
    /// Returns <see cref="InternetStatus.Online"/> when any endpoint matches, <see cref="InternetStatus.CaptivePortal"/>
    /// when something answered but nothing matched, and <see cref="InternetStatus.NoInternet"/> when nothing answered.
    /// </summary>
    /// <param name="observations">The results of one probe, one per endpoint.</param>
    public static InternetStatus Evaluate(IReadOnlyList<ProbeObservation> observations)
    {
        ArgumentNullException.ThrowIfNull(observations);

        if (observations.Any(static item => item.Responded && item.StatusCode == 200 &&
            string.Equals(item.Body, item.Endpoint.ExpectedBody, StringComparison.Ordinal)))
        {
            return InternetStatus.Online;
        }

        return observations.Any(static item => item.Responded) ? InternetStatus.CaptivePortal : InternetStatus.NoInternet;
    }
}
