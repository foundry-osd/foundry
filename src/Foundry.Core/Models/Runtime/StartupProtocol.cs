// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Models.Runtime;

/// <summary>Names and limits shared by the boot parent and managed runtime entry points.</summary>
public static class StartupProtocol
{
    public const int Version = 1;
    public const int MaximumFileBytes = 8192;
    public const string ManifestFileName = "foundry.startup.json";
    public const string ProtocolEnvironmentVariable = "FOUNDRY_STARTUP_PROTOCOL";
    public const string LaunchIdEnvironmentVariable = "FOUNDRY_STARTUP_LAUNCH_ID";
    public const string StatusPathEnvironmentVariable = "FOUNDRY_STARTUP_STATUS_PATH";
    public const string SessionIdEnvironmentVariable = "FOUNDRY_DIAGNOSTIC_SESSION_ID";
}

/// <summary>Ordered managed startup acknowledgements; failure may follow any acknowledged stage.</summary>
public static class StartupStage
{
    public const string ManagedStarted = "managed_started";
    public const string ConfigurationLoaded = "configuration_loaded";
    public const string UiReady = "ui_ready";
    public const string StartupFailed = "startup_failed";
}

/// <summary>Distinguishes explicit capabilities from legacy and invalid payloads.</summary>
public enum StartupCapabilityMode
{
    Legacy,
    Supported,
    Incompatible,
    Invalid
}

/// <summary>Only Supported authorizes waiting for a protocol acknowledgement.</summary>
public sealed record StartupCapabilityResult(StartupCapabilityMode Mode, int? ProtocolVersion = null);
