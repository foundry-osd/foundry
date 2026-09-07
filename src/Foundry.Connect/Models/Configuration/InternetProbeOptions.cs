// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Text;
using System.Text.Json.Serialization;
using Foundry.Core.Models.Configuration;
using Foundry.Utilities.Diagnostics;

namespace Foundry.Connect.Models.Configuration;

/// <summary>Defines bounded connectivity checks and preserves unsafe legacy configuration as non-ready.</summary>
public sealed class InternetProbeOptions
{
    public IReadOnlyList<ConnectInternetProbeEndpoint>? Probes { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? ProbeUris { get; init; }

    public int TimeoutSeconds { get; init; } = 5;

    [JsonIgnore]
    public string? ConfigurationErrorCode { get; init; }

    [JsonIgnore]
    public IReadOnlyList<string> InvalidEndpointUris { get; init; } = [];

    internal InternetProbeOptions Normalize()
    {
        List<ConnectInternetProbeEndpoint> probes = [];
        List<string> invalid = [];
        if (Probes is not null)
        {
            probes.AddRange(Probes);
        }
        else if (ProbeUris is not null)
        {
            foreach (string legacy in ProbeUris)
            {
                if (IsKnownLegacyUri(legacy, "www.msftconnecttest.com", "/connecttest.txt"))
                    probes.Add(ConnectInternetProbeEndpoint.Microsoft);
                else if (!IsKnownLegacyUri(legacy, "www.google.com", "/"))
                    invalid.Add(SafeEndpoint(legacy));
            }
        }
        else
        {
            probes.Add(ConnectInternetProbeEndpoint.Microsoft);
        }
        foreach (ConnectInternetProbeEndpoint? endpoint in probes)
        {
            if (!IsValidEndpoint(endpoint)) invalid.Add(SafeEndpoint(endpoint?.Uri));
        }
        return new InternetProbeOptions
        {
            Probes = probes.Distinct().ToArray(),
            TimeoutSeconds = TimeoutSeconds,
            ConfigurationErrorCode = ConfigurationErrorCode is not null || invalid.Count > 0 || probes.Count == 0 || TimeoutSeconds is < 1 or > 30
                ? "network_probe_configuration_invalid" : null,
            InvalidEndpointUris = InvalidEndpointUris.Concat(invalid).Distinct(StringComparer.Ordinal).ToArray()
        };
    }

    private static bool IsKnownLegacyUri(string? value, string host, string path) =>
        Uri.TryCreate(value?.Trim(), UriKind.Absolute, out Uri? uri) && uri.Scheme == "http" &&
        uri.Host.Equals(host, StringComparison.OrdinalIgnoreCase) && uri.IsDefaultPort &&
        uri.AbsolutePath.Equals(path, StringComparison.Ordinal) && string.IsNullOrEmpty(uri.UserInfo) &&
        string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment);
    private static bool IsValidEndpoint(ConnectInternetProbeEndpoint? endpoint)
    {
        if (endpoint is null || !System.Uri.TryCreate(endpoint.Uri, UriKind.Absolute, out Uri? uri) ||
            uri.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(uri.UserInfo) ||
            endpoint.ExpectedStatusCode is < 200 or > 299 || endpoint.ExpectedBody is null ||
            (endpoint.ExpectedBody.Length == 0 && endpoint.ExpectedStatusCode != 204)) return false;
        try { return new UTF8Encoding(false, true).GetByteCount(endpoint.ExpectedBody) <= 4096; }
        catch (EncoderFallbackException) { return false; }
    }

    private static string SafeEndpoint(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) && uri.Scheme is "http" or "https"
            ? LogValueSanitizer.SanitizeUri(uri) : "<invalid-uri>";
}
