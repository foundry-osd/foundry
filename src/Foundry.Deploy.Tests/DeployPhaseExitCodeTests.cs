// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Runtime;
using Foundry.Deploy.DependencyInjection;

namespace Foundry.Deploy.Tests;

public sealed class DeployPhaseExitCodeTests
{
    [Theory]
    [InlineData("configuration", (int)ChildRuntimeExitCode.ConfigurationFailure)]
    [InlineData("managed_startup", (int)ChildRuntimeExitCode.StartupFailure)]
    [InlineData("services", (int)ChildRuntimeExitCode.StartupFailure)]
    [InlineData("ui_startup", (int)ChildRuntimeExitCode.StartupFailure)]
    [InlineData("ui_initialization", (int)ChildRuntimeExitCode.StartupFailure)]
    public void StartupFailures_UseTheConnectExitCodes(string stage, int expected)
        => Assert.Equal(expected, DeployPhase.FailureExitCodeFor(stage));

    [Fact]
    public void ContainerFaults_AreNotReportedAsConfigurationFailures()
        => Assert.NotEqual((int)ChildRuntimeExitCode.ConfigurationFailure, DeployPhase.FailureExitCodeFor("services"));

    [Fact]
    public async Task ShellEnd_EndsThePhaseAsAnOperatorAbort()
    {
        var lifetime = new DeployPhaseLifetime();

        ServiceCollectionExtensions.CreateShellEndAction(lifetime)();

        Assert.Equal((int)ChildRuntimeExitCode.UserAborted, await lifetime.Completion);
    }

    [Fact]
    public void UnhandledUiException_KeepsTheCrashCode()
        => Assert.Equal(DeployPhase.UnhandledExceptionExitCode, DeployPhase.FailureExitCodeFor("dispatcher"));
}
