// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Configuration;

namespace Foundry.Core.Tests.Configuration;

public sealed class MachineNameComposerTests
{
    [Fact]
    public void Compose_WhenSerialExceedsFifteenCharacters_KeepsRightmostCharacters()
    {
        MachineNameCompositionResult result = Compose(
            [Hardware(MachineNameComponentType.SerialNumber, 15, MachineNameTruncation.KeepRight)],
            new Dictionary<MachineNameComponentType, string?>
            {
                [MachineNameComponentType.SerialNumber] = "SERIAL-123456789012345"
            });

        Assert.True(result.IsSuccess);
        Assert.Equal("123456789012345", result.ComputerName);
    }

    [Fact]
    public void Compose_WhenComponentsUseSeparatorAndUppercase_JoinsInOrder()
    {
        MachineNameCompositionResult result = Compose(
            [
                new MachineNameComponentSettings { Type = MachineNameComponentType.StaticText, StaticText = "pc" },
                Hardware(MachineNameComponentType.Model, 4, MachineNameTruncation.KeepLeft),
                Hardware(MachineNameComponentType.SerialNumber, 6, MachineNameTruncation.KeepRight)
            ],
            new Dictionary<MachineNameComponentType, string?>
            {
                [MachineNameComponentType.Model] = "Latitude 7450",
                [MachineNameComponentType.SerialNumber] = "ABC-123456"
            },
            MachineNameSeparator.Hyphen,
            MachineNameCasing.Uppercase);

        Assert.True(result.IsSuccess);
        Assert.Equal("PC-LATI-123456", result.ComputerName);
    }

    [Fact]
    public void Compose_WhenPreservingCasing_RemovesUnsupportedCharactersBeforeCropping()
    {
        MachineNameCompositionResult result = Compose(
            [Hardware(MachineNameComponentType.Model, 5, MachineNameTruncation.KeepRight)],
            new Dictionary<MachineNameComponentType, string?>
            {
                [MachineNameComponentType.Model] = "Model / 12345"
            });

        Assert.Equal("12345", result.ComputerName);
    }

    [Theory]
    [InlineData(null, MachineNameCompositionFailureKind.MissingHardwareValue)]
    [InlineData("Unknown", MachineNameCompositionFailureKind.PlaceholderHardwareValue)]
    [InlineData("To Be Filled By O.E.M.", MachineNameCompositionFailureKind.PlaceholderHardwareValue)]
    [InlineData("___", MachineNameCompositionFailureKind.EmptyAfterSanitization)]
    public void Compose_WhenHardwareValueCannotBeUsed_ReturnsSpecificFailure(
        string? value,
        MachineNameCompositionFailureKind expectedFailure)
    {
        MachineNameCompositionResult result = Compose(
            [Hardware(MachineNameComponentType.AssetTag, 8, MachineNameTruncation.KeepLeft)],
            new Dictionary<MachineNameComponentType, string?>
            {
                [MachineNameComponentType.AssetTag] = value
            });

        Assert.False(result.IsSuccess);
        Assert.Equal(expectedFailure, result.FailureKind);
        Assert.Equal(MachineNameComponentType.AssetTag, result.ComponentType);
    }

    [Fact]
    public void Compose_WhenRandomValueIsInvalid_ReturnsInvalidRandomValue()
    {
        MachineNameCompositionResult result = Compose(
            [new MachineNameComponentSettings { Type = MachineNameComponentType.Random, MaximumLength = 6 }],
            new Dictionary<MachineNameComponentType, string?>(),
            randomValue: "BAD_12");

        Assert.False(result.IsSuccess);
        Assert.Equal(MachineNameCompositionFailureKind.InvalidRandomValue, result.FailureKind);
    }

    [Fact]
    public void Compose_WhenLowercaseIsSelected_AppliesItToFinalName()
    {
        MachineNameCompositionResult result = Compose(
            [Hardware(MachineNameComponentType.Manufacturer, 4, MachineNameTruncation.KeepLeft)],
            new Dictionary<MachineNameComponentType, string?>
            {
                [MachineNameComponentType.Manufacturer] = "DELL"
            },
            casing: MachineNameCasing.Lowercase);

        Assert.Equal("dell", result.ComputerName);
    }

    private static MachineNameComponentSettings Hardware(
        MachineNameComponentType type,
        int maximumLength,
        MachineNameTruncation truncation) => new()
        {
            Type = type,
            MaximumLength = maximumLength,
            Truncation = truncation
        };

    private static MachineNameCompositionResult Compose(
        IReadOnlyList<MachineNameComponentSettings> components,
        IReadOnlyDictionary<MachineNameComponentType, string?> values,
        MachineNameSeparator separator = MachineNameSeparator.None,
        MachineNameCasing casing = MachineNameCasing.Preserve,
        string randomValue = "ABC123") => MachineNameComposer.Compose(new MachineNameCompositionRequest
        {
            Components = components,
            Values = values,
            Separator = separator,
            Casing = casing,
            RandomValue = randomValue
        });
}
