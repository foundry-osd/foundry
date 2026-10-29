// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Reflection;
using System.Runtime.InteropServices;
using Foundry.Connect.Network.Native;

namespace Foundry.Connect.Tests.Network;

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

    [Fact]
    public void WlanImports_SearchSystem32Only()
    {
        MethodInfo[] imports = typeof(NativeWifiApi)
            .GetMethods(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
            .Where(static method => method.Attributes.HasFlag(MethodAttributes.PinvokeImpl))
            .ToArray();

        Assert.Contains(imports, static method => method.Name == "WlanConnect");
        Assert.Contains(imports, static method => method.Name == "WlanSetProfile");
        Assert.All(imports, static method => Assert.Equal(
            DllImportSearchPath.System32,
            method.GetCustomAttribute<DefaultDllImportSearchPathsAttribute>()?.Paths));
    }

    // Layouts from wlanapi.h (SDK 10.0.26100.0); Connect ships x64 and ARM64 only.
    [Fact]
    public void NotificationStructures_MatchWlanapiLayout()
    {
        Assert.True(Environment.Is64BitProcess);
        Assert.Equal(40, Marshal.SizeOf<NativeWifiApi.WlanNotificationData>());
        Assert.Equal(40, Marshal.SizeOf<NativeWifiApi.WlanConnectionParameters>());
        Assert.Equal(568, Marshal.SizeOf<NativeWifiApi.WlanConnectionNotificationHeader>());
    }
}
