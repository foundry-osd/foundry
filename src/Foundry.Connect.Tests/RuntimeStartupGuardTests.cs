using Foundry.Connect.Services.Runtime;

namespace Foundry.Connect.Tests;

public sealed class RuntimeStartupGuardTests
{
    [Theory]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(false, false, false)]
    public void CanRun_ReturnsExpectedPolicy(bool isWinPeRuntime, bool isDebuggerBypassEnabled, bool expected)
    {
        bool canRun = RuntimeStartupGuard.CanRun(isWinPeRuntime, isDebuggerBypassEnabled);

        Assert.Equal(expected, canRun);
    }
}
