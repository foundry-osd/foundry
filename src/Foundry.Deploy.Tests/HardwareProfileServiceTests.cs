using Foundry.Deploy.Models;
using Foundry.Deploy.Services.Hardware;
using Microsoft.Extensions.Logging.Abstractions;

namespace Foundry.Deploy.Tests;

public sealed class HardwareProfileServiceTests
{
    [Fact]
    public async Task GetCurrentAsync_WhenNativeSourceReturnsHardwareData_MapsProfile()
    {
        var source = new FakeHardwareProfileSource
        {
            Snapshot = new HardwareProfileSnapshot(
                Manufacturer: " Dell Inc. ",
                Model: " Latitude 7450 ",
                Product: " 1.0 ",
                SerialNumber: " ABC123 ",
                IsOnBattery: true,
                Devices:
                [
                    new PnpDeviceInfo
                    {
                        Name = "System Firmware",
                        DeviceId = @"UEFI\RES_{9f41a8c2-5f6c-4f1d-9b8c-7a6e5d4c3b2a}",
                        ClassGuid = "{f2e7dd72-6468-4e36-b6f1-6488f42c1b52}",
                        HardwareIds = [@"UEFI\RES_{9f41a8c2-5f6c-4f1d-9b8c-7a6e5d4c3b2a}"]
                    },
                    new PnpDeviceInfo
                    {
                        Name = "Trusted Platform Module 2.0",
                        DeviceId = @"ACPI\MSFT0101\1",
                        PnpClass = "SecurityDevices",
                        HardwareIds = [@"ACPI\MSFT0101"]
                    }
                ])
        };
        var service = new HardwareProfileService(
            source,
            NullLogger<HardwareProfileService>.Instance,
            () => "AMD64");

        HardwareProfile profile = await service.GetCurrentAsync(TestContext.Current.CancellationToken);

        Assert.Equal("Dell", profile.Manufacturer);
        Assert.Equal("Latitude 7450", profile.Model);
        Assert.Equal("1.0", profile.Product);
        Assert.Equal("ABC123", profile.SerialNumber);
        Assert.Equal("x64", profile.Architecture);
        Assert.True(profile.IsOnBattery);
        Assert.True(profile.IsTpmPresent);
        Assert.Equal("9f41a8c2-5f6c-4f1d-9b8c-7a6e5d4c3b2a", profile.SystemFirmwareHardwareId);
        Assert.Equal(2, profile.PnpDevices.Count);
        Assert.Equal(1, source.Calls);
    }

    [Fact]
    public async Task GetCurrentAsync_WhenNativeSourceFails_ReturnsFallbackProfile()
    {
        var source = new FakeHardwareProfileSource
        {
            Exception = new InvalidOperationException("SetupAPI unavailable")
        };
        var service = new HardwareProfileService(
            source,
            NullLogger<HardwareProfileService>.Instance,
            () => string.Empty);

        HardwareProfile profile = await service.GetCurrentAsync(TestContext.Current.CancellationToken);

        Assert.Equal("Unknown", profile.Manufacturer);
        Assert.Equal("Unknown", profile.Model);
        Assert.Equal("Unknown", profile.Product);
        Assert.Equal("Unknown", profile.SerialNumber);
        Assert.False(profile.IsOnBattery);
        Assert.False(profile.IsTpmPresent);
        Assert.Empty(profile.SystemFirmwareHardwareId);
        Assert.Empty(profile.PnpDevices);
    }

    private sealed class FakeHardwareProfileSource : IHardwareProfileSource
    {
        public HardwareProfileSnapshot Snapshot { get; init; } = new(
            Manufacturer: string.Empty,
            Model: string.Empty,
            Product: string.Empty,
            SerialNumber: string.Empty,
            IsOnBattery: false,
            Devices: []);

        public Exception? Exception { get; init; }
        public int Calls { get; private set; }

        public HardwareProfileSnapshot Capture()
        {
            Calls++;
            if (Exception is not null)
            {
                throw Exception;
            }

            return Snapshot;
        }
    }
}
