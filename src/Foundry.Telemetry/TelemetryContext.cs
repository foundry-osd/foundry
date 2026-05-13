namespace Foundry.Telemetry;

/// <summary>
/// Provides common low-cardinality context added to every telemetry event.
/// </summary>
/// <param name="App">Stable application identifier.</param>
/// <param name="AppVersion">Application version reported by the running binary.</param>
/// <param name="BuildConfiguration">Compile-time build configuration of the running binary.</param>
/// <param name="Runtime">Runtime environment category.</param>
/// <param name="RuntimePayloadSource">Source of the Connect or Deploy runtime payload.</param>
/// <param name="BootMediaTarget">Boot media target or explicit WinPE runtime mode.</param>
/// <param name="RuntimeArchitecture">Application or runtime architecture.</param>
/// <param name="Locale">Current UI or runtime culture.</param>
/// <param name="SessionId">Random per-process identifier for grouping events from one run.</param>
public sealed record TelemetryContext(
    string App,
    string AppVersion,
    string BuildConfiguration,
    string Runtime,
    string RuntimePayloadSource,
    string BootMediaTarget,
    string RuntimeArchitecture,
    string Locale,
    string SessionId);
