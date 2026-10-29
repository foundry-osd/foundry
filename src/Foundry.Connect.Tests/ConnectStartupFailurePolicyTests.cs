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
    [InlineData(ChildRuntimeExitCode.Success, (int)ChildRuntimeExitCode.RuntimeFailure, (int)ChildRuntimeExitCode.Success)]
    [InlineData(ChildRuntimeExitCode.UserAborted, (int)ChildRuntimeExitCode.StartupFailure, (int)ChildRuntimeExitCode.UserAborted)]
    [InlineData(null, (int)ChildRuntimeExitCode.RuntimeFailure, (int)ChildRuntimeExitCode.RuntimeFailure)]
    public void ResolveProcessExitCode_AcceptedExitRequestWins(ChildRuntimeExitCode? requested, int fallback, int expected)
        => Assert.Equal(expected, ConnectStartupFailurePolicy.ResolveProcessExitCode(requested, fallback));
}
