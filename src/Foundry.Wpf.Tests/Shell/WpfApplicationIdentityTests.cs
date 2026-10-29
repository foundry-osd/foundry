// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Reflection;
using Foundry.Wpf.Shell;

namespace Foundry.Wpf.Tests.Shell;

public sealed class WpfApplicationIdentityTests
{
    [Fact]
    public void FromAssembly_ReturnsTrimmedInformationalVersion()
    {
        Assembly assembly = typeof(WpfApplicationIdentityTests).Assembly;
        string expected = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion.Trim();

        Assert.Equal(expected, WpfApplicationIdentity.FromAssembly(assembly).Version);
    }
}
