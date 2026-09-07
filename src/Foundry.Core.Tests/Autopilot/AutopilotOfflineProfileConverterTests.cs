// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Text.Json;
using Foundry.Core.Services.Autopilot;

namespace Foundry.Core.Tests.Autopilot;

public sealed class AutopilotOfflineProfileConverterTests
{
    private static readonly AutopilotTenantIdentity Tenant = new(Guid.Parse("11111111-1111-4111-8111-111111111111"), "example.onmicrosoft.com");
    private static readonly AutopilotOfflineProfileSource Profile = new()
    {
        Id = Guid.Parse("22222222-2222-4222-8222-222222222222"),
        DisplayName = "Synthetic",
        Mode = AutopilotOfflineDeploymentMode.UserDriven,
        JoinType = AutopilotOfflineJoinType.Entra
    };

    [Theory]
    [InlineData(AutopilotOfflineJoinType.Entra, 0)]
    [InlineData(AutopilotOfflineJoinType.Hybrid, 1)]
    public void Convert_UserDrivenProfile_PreservesSupportedFlagsAndJoin(AutopilotOfflineJoinType join, int expectedJoin)
    {
        string json = AutopilotOfflineProfileConverter.Convert(Profile with
        {
            JoinType = join,
            HideEscapeLink = true,
            HidePrivacySettings = true,
            HideEula = true,
            UserType = AutopilotOfflineUserType.Standard,
            SkipKeyboardSelectionPage = true,
            Language = "en-US"
        }, Tenant);
        using JsonDocument document = JsonDocument.Parse(json);
        Assert.Equal(expectedJoin, document.RootElement.GetProperty("CloudAssignedDomainJoinMethod").GetInt32());
        Assert.Equal(1310, document.RootElement.GetProperty("CloudAssignedOobeConfig").GetInt32());
        Assert.Equal("en-US", document.RootElement.GetProperty("CloudAssignedLanguage").GetString());
        Assert.Equal(Tenant.Id.ToString(), document.RootElement.GetProperty("CloudAssignedTenantId").GetString());
        Assert.All(json, character => Assert.True(character <= 127));
    }

    [Theory]
    [InlineData(AutopilotOfflineDeploymentMode.SelfDeploying)]
    [InlineData(AutopilotOfflineDeploymentMode.PreProvisioning)]
    [InlineData((AutopilotOfflineDeploymentMode)99)]
    public void Convert_UnsupportedRequestedOfflineMode_Rejects(AutopilotOfflineDeploymentMode mode)
        => Assert.Throws<InvalidDataException>(() => AutopilotOfflineProfileConverter.Convert(Profile with { Mode = mode }, Tenant));

    [Fact]
    public void Convert_PreprovisioningPermission_DoesNotChangeRequestedUserDrivenMode()
    {
        string json = AutopilotOfflineProfileConverter.Convert(Profile with { PreprovisioningAllowed = true }, Tenant);
        using JsonDocument document = JsonDocument.Parse(json);
        Assert.Equal(264, document.RootElement.GetProperty("CloudAssignedOobeConfig").GetInt32());
    }
}
