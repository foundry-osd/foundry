// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.

using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Configuration;
using Foundry.Deploy.Models;
using Foundry.Deploy.Models.Configuration;
using Foundry.Deploy.Services.System;

namespace Foundry.Deploy.Tests;

public sealed class MachineNamePreparationServiceTests
{
    [Fact]
    public void Prepare_WhenComposed_UsesHardwareAndOneRandomValue()
    {
        var settings = new DeployMachineNamingSettings
        {
            IsEnabled = true,
            Mode = MachineNamingMode.Composed,
            Components =
            [
                new MachineNameComponentSettings { Type = MachineNameComponentType.StaticText, StaticText = "PC" },
                new MachineNameComponentSettings
                {
                    Type = MachineNameComponentType.SerialNumber,
                    MaximumLength = 6,
                    Truncation = MachineNameTruncation.KeepRight
                },
                new MachineNameComponentSettings { Type = MachineNameComponentType.Random, MaximumLength = 3 }
            ],
            Separator = MachineNameSeparator.Hyphen,
            Casing = MachineNameCasing.Uppercase
        };
        var hardware = new HardwareProfile { SerialNumber = "SERIAL123456" };

        MachineNamePreparationResult result = MachineNamePreparationService.Prepare(
            settings,
            "OFFLINE-PC",
            hardware,
            _ => "abc");

        Assert.True(result.IsSuccess);
        Assert.Equal("PC-123456-ABC", result.ComputerName);
    }

    [Fact]
    public void Prepare_WhenRequiredHardwareIsPlaceholder_ReturnsFailure()
    {
        var settings = new DeployMachineNamingSettings
        {
            IsEnabled = true,
            Mode = MachineNamingMode.Composed,
            Components =
            [
                new MachineNameComponentSettings
                {
                    Type = MachineNameComponentType.AssetTag,
                    MaximumLength = 8,
                    Truncation = MachineNameTruncation.KeepLeft
                }
            ]
        };

        MachineNamePreparationResult result = MachineNamePreparationService.Prepare(
            settings,
            "OFFLINE-PC",
            new HardwareProfile { AssetTag = "Unknown" },
            _ => "ABC123");

        Assert.False(result.IsSuccess);
        Assert.Equal(MachineNameCompositionFailureKind.PlaceholderHardwareValue, result.FailureKind);
    }

    [Fact]
    public void Prepare_WhenManualInitialValueIsEmpty_UsesOfflineName()
    {
        var settings = new DeployMachineNamingSettings
        {
            IsEnabled = true,
            Mode = MachineNamingMode.Manual
        };

        MachineNamePreparationResult result = MachineNamePreparationService.Prepare(
            settings,
            "OFFLINE-PC",
            hardware: null,
            _ => "ABC123");

        Assert.Equal("OFFLINE-PC", result.ComputerName);
    }
}
