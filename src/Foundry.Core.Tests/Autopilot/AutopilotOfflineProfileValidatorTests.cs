// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Text.Json.Nodes;
using Foundry.Core.Services.Autopilot;
using Foundry.Core.Services.Configuration;
using Foundry.Core.Tests.TestUtilities;

namespace Foundry.Core.Tests.Autopilot;

public sealed class AutopilotOfflineProfileValidatorTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Validate_UserDrivenEntraAndHybridProfiles_AcceptsHarmlessExtensions(int join)
    {
        JsonObject document = JsonNode.Parse(AutopilotOfflineProfileTestData.ValidJson)!.AsObject();
        document["CloudAssignedDomainJoinMethod"] = join;
        document["FutureDisplayHint"] = "preserved";
        AutopilotOfflineProfileValidator.Validate(document.ToJsonString());
    }

    [Theory]
    [InlineData("CloudAssignedTenantId")]
    [InlineData("CloudAssignedTenantDomain")]
    [InlineData("CloudAssignedDomainJoinMethod")]
    [InlineData("CloudAssignedOobeConfig")]
    [InlineData("CloudAssignedForcedEnrollment")]
    [InlineData("ZtdCorrelationId")]
    [InlineData("CloudAssignedAadServerData")]
    public void Validate_MissingRequiredSemantics_RejectsBeforeProfileCreation(string property)
    {
        JsonObject document = JsonNode.Parse(AutopilotOfflineProfileTestData.ValidJson)!.AsObject();
        document.Remove(property);
        Assert.Throws<InvalidDataException>(() => AutopilotOfflineProfileValidator.Validate(document.ToJsonString()));
    }

    [Theory]
    [InlineData("CloudAssignedTenantId", "\"not-a-guid\"")]
    [InlineData("ZtdCorrelationId", "\"00000000-0000-0000-0000-000000000000\"")]
    [InlineData("CloudAssignedTenantDomain", "\"https://example.test\"")]
    [InlineData("CloudAssignedDomainJoinMethod", "2")]
    [InlineData("CloudAssignedForcedEnrollment", "\"1\"")]
    [InlineData("CloudAssignedForcedEnrollment", "2")]
    [InlineData("CloudAssignedOobeConfig", "96")]
    [InlineData("CloudAssignedOobeConfig", "2048")]
    [InlineData("Version", "2050")]
    [InlineData("CloudAssignedAadServerData", "\"{}\"")]
    public void Validate_InvalidFieldOrUnsupportedModeFlags_Rejects(string property, string value)
    {
        JsonObject document = JsonNode.Parse(AutopilotOfflineProfileTestData.ValidJson)!.AsObject();
        document[property] = JsonNode.Parse(value);
        Assert.Throws<InvalidDataException>(() => AutopilotOfflineProfileValidator.Validate(document.ToJsonString()));
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("{ malformed")]
    public void Validate_InvalidRoot_ThrowsPreciseInvalidData(string json)
        => Assert.Throws<InvalidDataException>(() => AutopilotOfflineProfileValidator.Validate(json));

    [Fact]
    public void Validate_BrandingTenantMismatch_Rejects()
    {
        string json = AutopilotOfflineProfileTestData.ValidJson.Replace("example.onmicrosoft.com\\", "other.onmicrosoft.com\\", StringComparison.Ordinal);
        Assert.Throws<InvalidDataException>(() => AutopilotOfflineProfileValidator.Validate(json));
    }

    [Fact]
    public void Factory_InvalidOfflineProfile_CannotBecomePersistedSettings()
        => Assert.Throws<InvalidDataException>(() => AutopilotProfileSettingsFactory.Create("id", "name", "{}", "Manual", DateTimeOffset.UnixEpoch));
}
