// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Text.Json.Serialization;

namespace Foundry.Core.Services.Autopilot;

public enum AutopilotOfflineJoinType { Entra, Hybrid }
public enum AutopilotOfflineDeploymentMode { UserDriven, SelfDeploying, PreProvisioning }
public enum AutopilotOfflineUserType { Administrator, Standard }

public sealed record AutopilotTenantIdentity(Guid Id, string DefaultDomain);

public sealed record AutopilotOfflineProfileSource
{
    public Guid Id { get; init; }
    public string DisplayName { get; init; } = string.Empty;
    public AutopilotOfflineJoinType JoinType { get; init; }
    public AutopilotOfflineDeploymentMode Mode { get; init; }
    public AutopilotOfflineUserType UserType { get; init; }
    public bool PreprovisioningAllowed { get; init; }
    public bool HideEscapeLink { get; init; }
    public bool HidePrivacySettings { get; init; }
    public bool HideEula { get; init; }
    public bool SkipKeyboardSelectionPage { get; init; }
    public bool HybridJoinSkipConnectivityCheck { get; init; }
    public string? DeviceNameTemplate { get; init; }
    public string? Language { get; init; }
}

internal sealed record CloudAssignedAadServerData(ZeroTouchConfig ZeroTouchConfig);
internal sealed record ZeroTouchConfig(string CloudAssignedTenantDomain, string CloudAssignedTenantUpn, int ForcedEnrollment);
internal sealed record OfflineAutopilotConfiguration
{
    [JsonPropertyName("Comment_File")]
    public required string CommentFile { get; init; }
    public int Version { get; init; } = 2049;
    public required string ZtdCorrelationId { get; init; }
    public int CloudAssignedDomainJoinMethod { get; init; }
    public int CloudAssignedOobeConfig { get; init; }
    public int CloudAssignedForcedEnrollment { get; init; }
    public required string CloudAssignedTenantId { get; init; }
    public required string CloudAssignedTenantDomain { get; init; }
    public required string CloudAssignedAadServerData { get; init; }
    public int CloudAssignedAutopilotUpdateDisabled { get; init; } = 1;
    public int CloudAssignedAutopilotUpdateTimeout { get; init; } = 1800000;
    public string? CloudAssignedDeviceName { get; init; }
    public string? CloudAssignedLanguage { get; init; }
    public string? CloudAssignedRegion { get; init; }
    [JsonPropertyName("HybridJoinSkipDCConnectivityCheck")]
    public int? HybridJoinSkipDcConnectivityCheck { get; init; }
}

[JsonSourceGenerationOptions(WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(CloudAssignedAadServerData))]
[JsonSerializable(typeof(OfflineAutopilotConfiguration))]
internal sealed partial class AutopilotOfflineJsonSerializerContext : JsonSerializerContext;
