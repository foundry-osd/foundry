// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Reflection;
using Foundry.Connect.Services.Network;

namespace Foundry.Connect.Tests;

public sealed class NativeWifiApiTests
{
    // wlanapi takes interface identifiers as const GUID*. A by-value Guid only reaches the callee as a
    // pointer under the x64 calling convention; ARM64 passes it in two registers and every call fails.
    [Fact]
    public void WlanImports_PassInterfaceGuidsByReference()
    {
        MethodInfo[] imports = typeof(NativeWifiApi)
            .GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
            .Where(static method => method.Attributes.HasFlag(MethodAttributes.PinvokeImpl))
            .ToArray();

        Assert.NotEmpty(imports);
        Assert.DoesNotContain(
            imports.SelectMany(static method => method.GetParameters()),
            static parameter => parameter.ParameterType == typeof(Guid));
    }
}
