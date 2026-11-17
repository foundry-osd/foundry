// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Runtime.InteropServices;
using Foundry.Utilities.Diagnostics;
using Xunit;

namespace Foundry.WinPE.Tests;

public sealed class BootstrapEnvironmentTests
{
    [Fact]
    public void AmbiguousCacheVolumesCannotSelectAnArbitraryRuntimeOrigin()
    {
        Assert.Throws<InvalidDataException>(() => BootstrapEnvironment.Create(@"X:\Foundry", "SESSION", Architecture.X64,
            [new BootstrapVolume(@"D:\", true, "Foundry Cache"), new BootstrapVolume(@"E:\", true, "Foundry Cache")]));
    }

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
    public void UsbCachePropagatesSessionPersistence()
    {
        BootstrapContext context = BootstrapEnvironment.Create(@"X:\Foundry", "SESSION", Architecture.X64,
            [new BootstrapVolume(@"D:\", true, "Foundry Cache")]);

        Assert.Equal(@"D:\Logs\SESSION", context.PersistenceDirectory);
        Assert.Equal("Usb", context.ChildEnvironment["FOUNDRY_DEPLOYMENT_MODE"]);
        Assert.Equal("SESSION", context.ChildEnvironment[DiagnosticSessionContext.EnvironmentVariableName]);
    }

    [Fact]
    public void IsoRemovesInheritedPersistence()
    {
        BootstrapContext context = BootstrapEnvironment.Create(@"X:\Foundry", "SESSION", Architecture.Arm64,
            [new BootstrapVolume(@"D:\", false, "Foundry Cache")]);

        Assert.Equal("win-arm64", context.RuntimeIdentifier);
        Assert.Null(context.ChildEnvironment[DiagnosticSessionContext.PersistenceDirectoryEnvironmentVariableName]);
        Assert.Equal("Iso", context.ChildEnvironment["FOUNDRY_DEPLOYMENT_MODE"]);
    }
}
