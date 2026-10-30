// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Connect.Network;
using Foundry.Connect.Network.Native;

namespace Foundry.Connect.Tests.Network;

public sealed class WlanClientMappingTests
{
    // The native enum is internal, so a public test method takes its numeric value (CS0051, revalidation C7).
    [Theory]
    [InlineData((int)NativeWifiApi.WlanInterfaceState.Connected, WlanInterfaceStatus.Connected)]
    [InlineData((int)NativeWifiApi.WlanInterfaceState.AdHocNetworkFormed, WlanInterfaceStatus.Connected)]
    [InlineData((int)NativeWifiApi.WlanInterfaceState.Associating, WlanInterfaceStatus.Connecting)]
    [InlineData((int)NativeWifiApi.WlanInterfaceState.Discovering, WlanInterfaceStatus.Connecting)]
    [InlineData((int)NativeWifiApi.WlanInterfaceState.Authenticating, WlanInterfaceStatus.Connecting)]
    [InlineData((int)NativeWifiApi.WlanInterfaceState.Disconnecting, WlanInterfaceStatus.Disconnecting)]
    [InlineData((int)NativeWifiApi.WlanInterfaceState.Disconnected, WlanInterfaceStatus.Disconnected)]
    [InlineData((int)NativeWifiApi.WlanInterfaceState.NotReady, WlanInterfaceStatus.NotReady)]
    public void MapInterfaceState_GroupsTransitionalStates(int state, WlanInterfaceStatus expected)
    {
        Assert.Equal(expected, WlanClient.MapInterfaceState((NativeWifiApi.WlanInterfaceState)state));
    }

    [Theory]
    [InlineData(0u, 0u, WlanOperationStatus.Succeeded)]
    [InlineData(1206u, 0x00080005u, WlanOperationStatus.ProfileRejected)]
    [InlineData(5u, 0u, WlanOperationStatus.RequestFailed)]
    [InlineData(1u, 0u, WlanOperationStatus.RequestFailed)]
    public void MapSetProfileResult_KeepsWin32AndReasonCodes(uint error, uint reason, WlanOperationStatus expected)
    {
        WlanOperationResult result = WlanClient.MapSetProfileResult(error, reason);

        Assert.Equal(expected, result.Status);
        Assert.Equal(error, result.Win32Error);
        Assert.Equal(reason, result.ReasonCode);
    }

    [Fact]
    public void MapConnectionNotification_UsesConnectionCompleteAsTerminal()
    {
        Assert.Equal(WlanOperationStatus.Succeeded, WlanClient.MapConnectionNotification(10, 0)!.Status);
        WlanOperationResult failure = WlanClient.MapConnectionNotification(10, 0x00038005)!;
        Assert.Equal(WlanOperationStatus.ConnectionFailed, failure.Status);
        Assert.Equal(0x00038005u, failure.ReasonCode);
        Assert.Null(WlanClient.MapConnectionNotification(11, 0x00038005));
        Assert.Null(WlanClient.MapConnectionNotification(9, 0));
    }

    [Fact]
    public void Snapshot_WhenOpenFails_IsRuntimeMissing()
    {
        WlanOperationResult open = WlanClient.MapOpenFailure(1062);

        Assert.Equal(WlanOperationStatus.RuntimeMissing, open.Status);
        Assert.Equal(1062u, open.Win32Error);
        Assert.Equal(WifiAvailability.RuntimeMissing, WlanSnapshot.RuntimeMissing(1062).Availability);
    }
}
