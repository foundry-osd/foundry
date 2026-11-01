// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.AutopilotInteractive.Startup;
using Foundry.Core.Models.Autopilot;

namespace Foundry.AutopilotInteractive.Tests.Startup;

public sealed class AutopilotInteractiveLogSessionTests
{
    [Fact]
    public void Resolve_PrefersTheConfiguredSessionId()
    {
        AutopilotInteractiveRuntimeConfiguration configuration = new() { CultureName = "en-US", DiagnosticSessionId = "DEPLOY-123" };

        Assert.Equal("DEPLOY-123", AutopilotInteractiveLogSession.Resolve(configuration, "FROM-ENV"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    public void Resolve_FallsBackToTheEnvironment_WhenConfigurationHasNone(string? configured)
    {
        AutopilotInteractiveRuntimeConfiguration configuration = new() { CultureName = "en-US", DiagnosticSessionId = configured };

        Assert.Equal("FROM-ENV", AutopilotInteractiveLogSession.Resolve(configuration, "FROM-ENV"));
        Assert.Equal("FROM-ENV", AutopilotInteractiveLogSession.Resolve(null, "FROM-ENV"));
    }

    [Fact]
    public void Resolve_GeneratesAnId_WhenNothingIsAvailable()
    {
        string sessionId = AutopilotInteractiveLogSession.Resolve(null, null);

        Assert.False(string.IsNullOrWhiteSpace(sessionId));
    }
}
