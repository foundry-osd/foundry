// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Utilities.Hardware;
using Foundry.Utilities.Processes;

namespace Foundry.Utilities.Tests.Hardware;

public sealed class WindowsHardwareInspectorTests
{
    [Fact]
    public async Task GetCurrentAsync_ParsesHardwareAndPnpFacts()
    {
        ProcessExecutionRequest? capturedRequest = null;
        var inspector = new WindowsHardwareInspector((request, _) =>
        {
            capturedRequest = request;
            return Task.FromResult(CreateSuccessResult(
                """
                {
                  "Manufacturer":" VMware, Inc. ",
                  "Model":" Virtual Machine ",
                  "Product":" Workstation ",
                  "SerialNumber":" SERIAL-1 ",
                  "Architecture":"AMD64",
                  "IsOnBattery":true,
                  "IsTpmPresent":"true",
                  "SystemFirmwareHardwareId":" UEFI\\RES_{FIRMWARE} ",
                  "PnpDevices":{
                    "Name":" Network Adapter ",
                    "DeviceId":" PCI\\VEN_1234 ",
                    "HardwareIds":[" PCI\\VEN_1234 "," "],
                    "ClassGuid":" {CLASS} ",
                    "Manufacturer":" Vendor ",
                    "PnpClass":" Net "
                  }
                }
                """));
        });

        HardwareSnapshot snapshot = await inspector.GetCurrentAsync(TestContext.Current.CancellationToken);

        Assert.Equal("VMware, Inc.", snapshot.Manufacturer);
        Assert.Equal("Virtual Machine", snapshot.Model);
        Assert.Equal("Workstation", snapshot.Product);
        Assert.Equal("SERIAL-1", snapshot.SerialNumber);
        Assert.Equal("x64", snapshot.Architecture);
        Assert.True(snapshot.IsVirtualMachine);
        Assert.True(snapshot.IsOnBattery);
        Assert.True(snapshot.IsTpmPresent);
        Assert.Equal("UEFI\\RES_{FIRMWARE}", snapshot.SystemFirmwareHardwareId);

        PnpDeviceSnapshot device = Assert.Single(snapshot.PnpDevices);
        Assert.Equal("Network Adapter", device.Name);
        Assert.Equal("PCI\\VEN_1234", device.DeviceId);
        Assert.Equal(["PCI\\VEN_1234"], device.HardwareIds);
        Assert.Equal("{CLASS}", device.ClassGuid);
        Assert.Equal("Vendor", device.Manufacturer);
        Assert.Equal("Net", device.PnpClass);

        Assert.NotNull(capturedRequest);
        Assert.Equal("powershell.exe", capturedRequest.FileName);
        Assert.Equal("-NoProfile", capturedRequest.ArgumentList?[0]);
        Assert.Contains("-EncodedCommand", capturedRequest.ArgumentList!);
    }

    [Fact]
    public async Task GetCurrentAsync_WhenRootIsSingleItemArray_ParsesSnapshot()
    {
        var inspector = CreateInspector("""[{"Manufacturer":"Dell","Architecture":"ARM64"}]""");

        HardwareSnapshot snapshot = await inspector.GetCurrentAsync(TestContext.Current.CancellationToken);

        Assert.Equal("Dell", snapshot.Manufacturer);
        Assert.Equal("arm64", snapshot.Architecture);
    }

    [Fact]
    public async Task GetCurrentAsync_WhenFieldsAreMissing_ReturnsNeutralFacts()
    {
        var inspector = CreateInspector("{}");

        HardwareSnapshot snapshot = await inspector.GetCurrentAsync(TestContext.Current.CancellationToken);

        Assert.Equal(string.Empty, snapshot.Manufacturer);
        Assert.Equal(string.Empty, snapshot.Model);
        Assert.Equal(string.Empty, snapshot.Product);
        Assert.Equal(string.Empty, snapshot.SerialNumber);
        Assert.Equal(string.Empty, snapshot.Architecture);
        Assert.False(snapshot.IsVirtualMachine);
        Assert.False(snapshot.IsOnBattery);
        Assert.False(snapshot.IsTpmPresent);
        Assert.Equal(string.Empty, snapshot.SystemFirmwareHardwareId);
        Assert.Empty(snapshot.PnpDevices);
    }

    [Theory]
    [InlineData("AMD64", "x64")]
    [InlineData("x64", "x64")]
    [InlineData("ARM64", "arm64")]
    [InlineData("aarch64", "arm64")]
    [InlineData("custom", "custom")]
    public async Task GetCurrentAsync_NormalizesArchitecture(string architecture, string expected)
    {
        var inspector = CreateInspector($$"""{"Architecture":"{{architecture}}"}""");

        HardwareSnapshot snapshot = await inspector.GetCurrentAsync(TestContext.Current.CancellationToken);

        Assert.Equal(expected, snapshot.Architecture);
    }

    [Theory]
    [InlineData("VMware, Inc.", "Physical", "Product", true)]
    [InlineData("Microsoft Corporation", "Virtual Machine", "Product", true)]
    [InlineData("Dell Inc.", "Latitude", "Laptop", false)]
    public async Task GetCurrentAsync_DetectsVirtualMachines(
        string manufacturer,
        string model,
        string product,
        bool expected)
    {
        var inspector = CreateInspector(
            $$"""{"Manufacturer":"{{manufacturer}}","Model":"{{model}}","Product":"{{product}}"}""");

        HardwareSnapshot snapshot = await inspector.GetCurrentAsync(TestContext.Current.CancellationToken);

        Assert.Equal(expected, snapshot.IsVirtualMachine);
    }

    [Fact]
    public async Task GetCurrentAsync_WhenProcessReturnsNoData_ThrowsInvalidDataException()
    {
        var inspector = new WindowsHardwareInspector((_, _) => Task.FromResult(
            new ProcessExecutionResult { ExitCode = 1, StandardError = "CIM failed" }));

        await Assert.ThrowsAsync<InvalidDataException>(
            () => inspector.GetCurrentAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetCurrentAsync_WhenPayloadIsMalformed_ThrowsInvalidDataException()
    {
        var inspector = CreateInspector("{");

        await Assert.ThrowsAsync<InvalidDataException>(
            () => inspector.GetCurrentAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetCurrentAsync_WhenExecutionIsCanceled_PropagatesCancellation()
    {
        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();
        var inspector = new WindowsHardwareInspector(
            (_, cancellationToken) => Task.FromCanceled<ProcessExecutionResult>(cancellationToken));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => inspector.GetCurrentAsync(cancellationSource.Token));
    }

    private static WindowsHardwareInspector CreateInspector(string json)
    {
        return new WindowsHardwareInspector((_, _) => Task.FromResult(CreateSuccessResult(json)));
    }

    private static ProcessExecutionResult CreateSuccessResult(string json)
    {
        return new ProcessExecutionResult
        {
            ExitCode = 0,
            StandardOutput = json
        };
    }
}
