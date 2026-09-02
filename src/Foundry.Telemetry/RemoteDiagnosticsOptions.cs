// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Telemetry;

/// <summary>
/// Describes runtime configuration for privacy-filtered remote diagnostics.
/// </summary>
/// <param name="IsEnabled">Whether the user explicitly enabled remote diagnostics.</param>
/// <param name="HostUrl">PostHog ingestion host.</param>
/// <param name="ProjectToken">Public PostHog project token.</param>
/// <param name="InstallId">Anonymous installation identifier.</param>
public sealed record RemoteDiagnosticsOptions(
    bool IsEnabled,
    string HostUrl,
    string ProjectToken,
    string InstallId)
{
    /// <summary>
    /// Gets whether the exporter has complete configuration and user consent.
    /// </summary>
    public bool CanSend =>
        IsEnabled &&
        Uri.TryCreate(HostUrl, UriKind.Absolute, out Uri? hostUri) &&
        (hostUri.Scheme == Uri.UriSchemeHttps || hostUri.Scheme == Uri.UriSchemeHttp) &&
        !string.IsNullOrWhiteSpace(ProjectToken) &&
        !string.IsNullOrWhiteSpace(InstallId);
}
