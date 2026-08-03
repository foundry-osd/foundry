// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Services.WinPe;

namespace Foundry.Core.Tests.WinPe;

public sealed class WinPeOptionalComponentDefaultsTests
{
    [Fact]
    public void OrderForIntegration_MovesSecureComponentsLastInOrder()
    {
        string[] components =
        [
            "WinPE-SecureStartup",
            "WinPE-WMI",
            "WinPE-SecureBootCmdlets",
            "WinPE-PowerShell"
        ];

        IReadOnlyList<string> ordered = WinPeOptionalComponentDefaults.OrderForIntegration(components);

        Assert.Equal(
            ["WinPE-WMI", "WinPE-PowerShell", "WinPE-SecureBootCmdlets", "WinPE-SecureStartup"],
            ordered);
    }

    [Fact]
    public void OrderForIntegration_PreservesOtherComponentOrderAndCasing()
    {
        string[] components = ["winpe-securestartup", "WinPE-NetFX", "WinPE-Scripting"];

        IReadOnlyList<string> ordered = WinPeOptionalComponentDefaults.OrderForIntegration(components);

        Assert.Equal(["WinPE-NetFX", "WinPE-Scripting", "winpe-securestartup"], ordered);
    }

    [Fact]
    public void OrderForIntegration_WhenNoSecureComponents_ReturnsSameOrder()
    {
        string[] components = ["WinPE-WMI", "WinPE-NetFX", "WinPE-Scripting"];

        IReadOnlyList<string> ordered = WinPeOptionalComponentDefaults.OrderForIntegration(components);

        Assert.Equal(components, ordered);
    }
}
