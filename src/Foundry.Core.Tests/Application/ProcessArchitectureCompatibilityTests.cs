// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Runtime.InteropServices;
using Foundry.Core.Services.Application;

namespace Foundry.Core.Tests.Application;

public sealed class ProcessArchitectureCompatibilityTests
{
    [Theory]
    [InlineData(Architecture.X64, Architecture.Arm64, true)]
    [InlineData(Architecture.X64, Architecture.X64, false)]
    [InlineData(Architecture.Arm64, Architecture.Arm64, false)]
    [InlineData(Architecture.X86, Architecture.Arm64, false)]
    public void RequiresArm64Build_IsTrueOnlyForX64BuildOnArm64Windows(
        Architecture processArchitecture,
        Architecture osArchitecture,
        bool expected)
    {
        Assert.Equal(expected, ProcessArchitectureCompatibility.RequiresArm64Build(processArchitecture, osArchitecture));
    }
}
