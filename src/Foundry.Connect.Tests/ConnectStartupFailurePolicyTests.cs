// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Connect.Startup;
using Foundry.Core.Models.Runtime;

namespace Foundry.Connect.Tests;

public sealed class ConnectStartupFailurePolicyTests
{
    [Theory]
    [InlineData(ConnectStartupStage.Configuration, ChildRuntimeExitCode.ConfigurationFailure)]
    [InlineData(ConnectStartupStage.Services, ChildRuntimeExitCode.StartupFailure)]
    [InlineData(ConnectStartupStage.UiStartup, ChildRuntimeExitCode.StartupFailure)]
    [InlineData(ConnectStartupStage.Running, ChildRuntimeExitCode.RuntimeFailure)]
    public void Map_StageToExitCode(ConnectStartupStage stage, ChildRuntimeExitCode expected)
        => Assert.Equal(expected, ConnectStartupFailurePolicy.Map(stage));

    [Theory]
    [InlineData(ConnectStartupStage.UiStartup, false, ChildRuntimeExitCode.StartupFailure)]
    [InlineData(ConnectStartupStage.UiStartup, true, ChildRuntimeExitCode.RuntimeFailure)]
    [InlineData(ConnectStartupStage.Configuration, false, ChildRuntimeExitCode.ConfigurationFailure)]
    public void MapFailure_OnceTheUiIsReady_IsARuntimeFailure(ConnectStartupStage stage, bool isUiReady, ChildRuntimeExitCode expected)
        => Assert.Equal(expected, ConnectStartupFailurePolicy.MapFailure(stage, isUiReady));

    [Theory]
    [InlineData(ChildRuntimeExitCode.UserAborted, ChildRuntimeExitCode.StartupFailure, ChildRuntimeExitCode.UserAborted)]
    [InlineData(ChildRuntimeExitCode.ConfigurationFailure, ChildRuntimeExitCode.RuntimeFailure, ChildRuntimeExitCode.ConfigurationFailure)]
    [InlineData(null, ChildRuntimeExitCode.RuntimeFailure, ChildRuntimeExitCode.RuntimeFailure)]
    public void ResolveFailureExitCode_AcceptedAbortOrFailureWins(
        ChildRuntimeExitCode? requested, ChildRuntimeExitCode failure, ChildRuntimeExitCode expected)
        => Assert.Equal((int)expected, ConnectStartupFailurePolicy.ResolveFailureExitCode(requested, failure));

    [Theory]
    [InlineData(ChildRuntimeExitCode.RuntimeFailure)]
    [InlineData(ChildRuntimeExitCode.StartupFailure)]
    public void ResolveFailureExitCode_AcceptedSuccessThenException_IsTheFailureNeverSuccess(ChildRuntimeExitCode failure)
    {
        int exitCode = ConnectStartupFailurePolicy.ResolveFailureExitCode(ChildRuntimeExitCode.Success, failure);

        Assert.Equal((int)failure, exitCode);
        Assert.NotEqual((int)ChildRuntimeExitCode.Success, exitCode);
    }
}
