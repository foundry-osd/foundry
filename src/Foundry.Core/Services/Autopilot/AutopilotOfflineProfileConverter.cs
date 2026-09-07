// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Text.Json;

namespace Foundry.Core.Services.Autopilot;

/// <summary>Converts normalized user-driven settings; online profile assignment is not inferred by this converter.</summary>
public static class AutopilotOfflineProfileConverter
{
    public static string Convert(AutopilotOfflineProfileSource source, AutopilotTenantIdentity tenant)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(tenant);
        if (source.Mode != AutopilotOfflineDeploymentMode.UserDriven)
            throw new InvalidDataException("Offline JSON supports only user-driven deployment; self-deploying and pre-provisioning require an assigned online profile.");
        if (!Enum.IsDefined(source.JoinType) || !Enum.IsDefined(source.UserType) || source.Id == Guid.Empty || tenant.Id == Guid.Empty)
            throw new InvalidDataException("The offline profile requires supported join/user settings and nonempty profile and tenant IDs.");
        if (source.HybridJoinSkipConnectivityCheck && source.JoinType != AutopilotOfflineJoinType.Hybrid)
            throw new InvalidDataException("Skipping domain-controller connectivity checks requires hybrid join.");
        AutopilotOfflineProfileValidator.ValidateDomain(tenant.DefaultDomain);
        int flags = 8 | 256;
        if (source.UserType == AutopilotOfflineUserType.Standard) flags |= 2;
        if (source.HidePrivacySettings) flags |= 4;
        if (source.HideEula) flags |= 16;
        if (source.SkipKeyboardSelectionPage) flags |= 1024;
        int forced = source.HideEscapeLink ? 1 : 0;
        string branding = JsonSerializer.Serialize(new CloudAssignedAadServerData(new(tenant.DefaultDomain, string.Empty, forced)),
            AutopilotOfflineJsonSerializerContext.Default.CloudAssignedAadServerData);
        var output = new OfflineAutopilotConfiguration
        {
            CommentFile = "Profile " + source.DisplayName,
            ZtdCorrelationId = source.Id.ToString("D"),
            CloudAssignedDomainJoinMethod = source.JoinType == AutopilotOfflineJoinType.Hybrid ? 1 : 0,
            CloudAssignedOobeConfig = flags,
            CloudAssignedForcedEnrollment = forced,
            CloudAssignedTenantId = tenant.Id.ToString("D"),
            CloudAssignedTenantDomain = tenant.DefaultDomain,
            CloudAssignedAadServerData = branding,
            CloudAssignedDeviceName = string.IsNullOrWhiteSpace(source.DeviceNameTemplate) ? null : source.DeviceNameTemplate,
            CloudAssignedLanguage = source.SkipKeyboardSelectionPage && !string.IsNullOrWhiteSpace(source.Language) ? source.Language : null,
            CloudAssignedRegion = source.SkipKeyboardSelectionPage && !string.IsNullOrWhiteSpace(source.Language) ? source.Language : null,
            HybridJoinSkipDcConnectivityCheck = source.HybridJoinSkipConnectivityCheck ? 1 : null
        };
        string json = JsonSerializer.Serialize(output, AutopilotOfflineJsonSerializerContext.Default.OfflineAutopilotConfiguration);
        AutopilotOfflineProfileValidator.Validate(json);
        return json;
    }
}
