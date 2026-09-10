// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Runtime.InteropServices;
using Foundry.Utilities.Diagnostics;
using Xunit;

namespace Foundry.Bootstrap.Tests;

public sealed class BootstrapEnvironmentTests
{
    [Theory]
    [InlineData(Architecture.X64, "win-x64")]
    [InlineData(Architecture.Arm64, "win-arm64")]
    public void SupportedArchitectureSelectsMatchingRuntime(Architecture architecture, string expected)
    {
        Assert.Equal(expected, BootstrapEnvironment.ResolveRuntimeIdentifier(architecture));
    }

    [Fact]
    public void UnsupportedArchitectureFailsBeforeLaunchingApplications()
    {
        Assert.Throws<PlatformNotSupportedException>(() => BootstrapEnvironment.ResolveRuntimeIdentifier(Architecture.X86));
    }

    [Fact]
    public void UsbCacheTakesPrecedenceAndPropagatesSessionPersistence()
    {
        BootstrapContext context = BootstrapEnvironment.Create(@"X:\Foundry", "SESSION", Architecture.X64,
            [new BootstrapVolume(@"D:\", true, "Foundry Cache")], _ => null);

        Assert.Equal(@"D:\Runtime", context.RuntimeRoot);
        Assert.Equal(@"D:\Logs\SESSION", context.PersistenceDirectory);
        Assert.Equal("Usb", context.ChildEnvironment["FOUNDRY_DEPLOYMENT_MODE"]);
        Assert.Equal("SESSION", context.ChildEnvironment[DiagnosticSessionContext.EnvironmentVariableName]);
    }

    [Fact]
    public void IsoRemovesInheritedPersistenceAndHonorsDebugSource()
    {
        BootstrapContext context = BootstrapEnvironment.Create(@"X:\Foundry", "SESSION", Architecture.Arm64,
            [new BootstrapVolume(@"D:\", false, "Foundry Cache")], path => path.Contains("connect") ? " debug\r\n" : "release");

        Assert.Equal(@"X:\Foundry\Runtime", context.RuntimeRoot);
        Assert.Null(context.ChildEnvironment[DiagnosticSessionContext.PersistenceDirectoryEnvironmentVariableName]);
        Assert.True(context.ConnectIsDebug);
        Assert.False(context.DeployIsDebug);
    }
}
