namespace Foundry.Telemetry;

/// <summary>
/// Describes runtime telemetry configuration after user preference and build token resolution.
/// </summary>
/// <param name="IsEnabled">Whether the user-facing telemetry setting allows event capture.</param>
/// <param name="HostUrl">The PostHog host URL used by the SDK client.</param>
/// <param name="ProjectToken">The public PostHog project token used for event ingestion.</param>
/// <param name="InstallId">The anonymous installation identifier shared by Foundry runtimes.</param>
public sealed record TelemetryOptions(
    bool IsEnabled,
    string HostUrl,
    string ProjectToken,
    string InstallId)
{
    /// <summary>
    /// Gets whether the telemetry client has enough non-sensitive configuration to send events.
    /// </summary>
    public bool CanSend =>
        IsEnabled &&
        Uri.TryCreate(HostUrl, UriKind.Absolute, out _) &&
        !string.IsNullOrWhiteSpace(ProjectToken) &&
        !string.IsNullOrWhiteSpace(InstallId);
}
