namespace Foundry.Telemetry;

/// <summary>
/// Defines the low-volume PostHog event names emitted by Foundry products.
/// </summary>
public static class TelemetryEvents
{
    public const string AppDailyActive = "app:daily_active";
    public const string OsdBootMediaFinished = "osd:boot_media_finished";
    public const string ConnectSessionReady = "connect:session_ready";
    public const string DeploySessionFinished = "deploy:session_finished";
}
