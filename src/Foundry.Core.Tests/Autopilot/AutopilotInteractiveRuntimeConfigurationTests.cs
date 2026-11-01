// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Autopilot;
using Foundry.Core.Services.Runtime;

namespace Foundry.Core.Tests.Autopilot;

public sealed class AutopilotInteractiveRuntimeConfigurationTests
{
    [Fact]
    public void Serializer_RoundTripsAllFields()
    {
        var configuration = new AutopilotInteractiveRuntimeConfiguration
        {
            AssignedComputerName = "FND-0001",
            ComputerName = "FND-0002",
            PreselectCustomGroupTag = true,
            OfferAssignedUser = true,
            Theme = RuntimeThemePreference.Dark,
            CultureName = "fr-FR",
            DiagnosticSessionId = "session-42",
            KeyboardLayoutId = "0000080C"
        };

        string json = AutopilotInteractiveRuntimeConfigurationSerializer.Serialize(configuration);
        AutopilotInteractiveRuntimeConfiguration result = AutopilotInteractiveRuntimeConfigurationSerializer.Deserialize(json);

        Assert.Contains("\"schemaVersion\": 1", json, StringComparison.Ordinal);
        Assert.Contains("\"theme\": \"Dark\"", json, StringComparison.Ordinal);
        Assert.Contains("\"keyboardLayoutId\": \"0000080C\"", json, StringComparison.Ordinal);
        Assert.Contains("\"computerName\": \"FND-0002\"", json, StringComparison.Ordinal);
        Assert.Equal(configuration, result);
    }

    [Fact]
    public void Serializer_RoundTripsWithoutOptionalNames()
    {
        var configuration = new AutopilotInteractiveRuntimeConfiguration { CultureName = "en-US" };

        string json = AutopilotInteractiveRuntimeConfigurationSerializer.Serialize(configuration);
        AutopilotInteractiveRuntimeConfiguration result = AutopilotInteractiveRuntimeConfigurationSerializer.Deserialize(json);

        Assert.Null(result.KeyboardLayoutId);
        Assert.Null(result.ComputerName);
        Assert.Equal(configuration, result);
    }

    [Fact]
    public void Deserialize_Throws_WhenSchemaVersionIsUnknown()
    {
        Assert.Throws<InvalidDataException>(() => AutopilotInteractiveRuntimeConfigurationSerializer.Deserialize(
            """{"schemaVersion":2,"cultureName":"en-US"}"""));
    }

    [Fact]
    public void Deserialize_Throws_WhenSchemaVersionIsMissing()
    {
        Assert.Throws<InvalidDataException>(() => AutopilotInteractiveRuntimeConfigurationSerializer.Deserialize(
            """{"cultureName":"en-US"}"""));
    }

    [Theory]
    [InlineData("""{"schemaVersion":1}""")]
    [InlineData("""{"schemaVersion":1,"cultureName":""}""")]
    [InlineData("""{"schemaVersion":1,"cultureName":"  "}""")]
    public void Deserialize_Throws_WhenCultureNameIsMissing(string json)
    {
        Assert.Throws<InvalidDataException>(() => AutopilotInteractiveRuntimeConfigurationSerializer.Deserialize(json));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("")]
    [InlineData("null")]
    public void Deserialize_Throws_WhenJsonIsMalformed(string json)
    {
        Assert.Throws<InvalidDataException>(() => AutopilotInteractiveRuntimeConfigurationSerializer.Deserialize(json));
    }

    [Fact]
    public void Deserialize_AllowsMissingOptionalFields()
    {
        AutopilotInteractiveRuntimeConfiguration result = AutopilotInteractiveRuntimeConfigurationSerializer.Deserialize(
            """{"schemaVersion":1,"cultureName":"en-US"}""");

        Assert.Null(result.AssignedComputerName);
        Assert.Null(result.ComputerName);
        Assert.False(result.PreselectCustomGroupTag);
        Assert.False(result.OfferAssignedUser);
        Assert.Null(result.DiagnosticSessionId);
        Assert.Null(result.KeyboardLayoutId);
        Assert.Equal("en-US", result.CultureName);
    }
}
