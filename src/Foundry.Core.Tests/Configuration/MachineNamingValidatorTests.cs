// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Configuration;

namespace Foundry.Core.Tests.Configuration;

public sealed class MachineNamingValidatorTests
{
    [Fact]
    public void Validate_WhenDisabled_IgnoresInactiveSettings()
    {
        var settings = new MachineNamingSettings
        {
            Components = [new MachineNameComponentSettings()]
        };

        MachineNamingValidationResult result = MachineNamingValidator.Validate(settings);

        Assert.True(result.IsValid);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void Validate_WhenManualInitialValueIsInvalid_ReturnsIssue()
    {
        var settings = new MachineNamingSettings
        {
            IsEnabled = true,
            Mode = MachineNamingMode.Manual,
            ManualInitialValue = "INVALID NAME"
        };

        MachineNamingValidationResult result = MachineNamingValidator.Validate(settings);

        Assert.Contains(result.Issues, issue => issue.Code == MachineNamingValidationCode.InvalidManualInitialValue);
    }

    [Fact]
    public void Validate_WhenComposedHasNoComponents_ReturnsIssue()
    {
        var settings = new MachineNamingSettings
        {
            IsEnabled = true,
            Mode = MachineNamingMode.Composed
        };

        MachineNamingValidationResult result = MachineNamingValidator.Validate(settings);

        Assert.Contains(result.Issues, issue => issue.Code == MachineNamingValidationCode.ComponentsRequired);
    }

    [Fact]
    public void Validate_WhenComponentTypeIsRepeated_ReturnsIndexedIssue()
    {
        var settings = CreateComposed(
            new MachineNameComponentSettings { Type = MachineNameComponentType.StaticText, StaticText = "PC" },
            new MachineNameComponentSettings { Type = MachineNameComponentType.StaticText, StaticText = "LAB" });

        MachineNamingValidationResult result = MachineNamingValidator.Validate(settings);

        Assert.Contains(result.Issues, issue =>
            issue.Code == MachineNamingValidationCode.DuplicateComponentType && issue.ComponentIndex == 1);
    }

    [Fact]
    public void Validate_WhenStaticTextBecomesEmptyAfterSanitization_ReturnsIssue()
    {
        var settings = CreateComposed(new MachineNameComponentSettings
        {
            Type = MachineNameComponentType.StaticText,
            StaticText = "___"
        });

        MachineNamingValidationResult result = MachineNamingValidator.Validate(settings);

        Assert.Contains(result.Issues, issue => issue.Code == MachineNamingValidationCode.StaticTextBecomesEmpty);
    }

    [Fact]
    public void Validate_WhenHardwareComponentHasNoLengthOrTruncation_ReturnsBothIssues()
    {
        var settings = CreateComposed(new MachineNameComponentSettings { Type = MachineNameComponentType.SerialNumber });

        MachineNamingValidationResult result = MachineNamingValidator.Validate(settings);

        Assert.Contains(result.Issues, issue => issue.Code == MachineNamingValidationCode.MaximumLengthRequired);
        Assert.Contains(result.Issues, issue => issue.Code == MachineNamingValidationCode.TruncationRequired);
    }

    [Fact]
    public void Validate_WhenRandomHasTruncation_ReturnsUnexpectedTruncation()
    {
        var settings = CreateComposed(new MachineNameComponentSettings
        {
            Type = MachineNameComponentType.Random,
            MaximumLength = 6,
            Truncation = MachineNameTruncation.KeepRight
        });

        MachineNamingValidationResult result = MachineNamingValidator.Validate(settings);

        Assert.Contains(result.Issues, issue => issue.Code == MachineNamingValidationCode.UnexpectedTruncation);
    }

    [Fact]
    public void Validate_WhenMaximumBudgetExceedsWindowsLimit_CountsSeparator()
    {
        MachineNamingSettings settings = CreateComposed(
            new MachineNameComponentSettings { Type = MachineNameComponentType.StaticText, StaticText = "PC" },
            new MachineNameComponentSettings
            {
                Type = MachineNameComponentType.SerialNumber,
                MaximumLength = 13,
                Truncation = MachineNameTruncation.KeepRight
            }) with
        {
            Separator = MachineNameSeparator.Hyphen
        };

        MachineNamingValidationResult result = MachineNamingValidator.Validate(settings);

        Assert.Equal(16, result.MaximumLength);
        Assert.Contains(result.Issues, issue => issue.Code == MachineNamingValidationCode.CharacterBudgetExceeded);
    }

    [Fact]
    public void Validate_WhenCompositionUsesExactlyFifteenCharacters_IsValid()
    {
        MachineNamingSettings settings = CreateComposed(
            new MachineNameComponentSettings { Type = MachineNameComponentType.StaticText, StaticText = "PC" },
            new MachineNameComponentSettings
            {
                Type = MachineNameComponentType.SerialNumber,
                MaximumLength = 12,
                Truncation = MachineNameTruncation.KeepRight
            }) with
        {
            Separator = MachineNameSeparator.Hyphen
        };

        MachineNamingValidationResult result = MachineNamingValidator.Validate(settings);

        Assert.True(result.IsValid);
        Assert.Equal(15, result.MaximumLength);
    }

    private static MachineNamingSettings CreateComposed(params MachineNameComponentSettings[] components) => new()
    {
        IsEnabled = true,
        Mode = MachineNamingMode.Composed,
        Components = components
    };
}
