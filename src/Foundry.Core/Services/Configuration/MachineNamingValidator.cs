// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;

namespace Foundry.Core.Services.Configuration;

/// <summary>
/// Validates structured machine-naming settings and their Windows name budget.
/// </summary>
public static class MachineNamingValidator
{
    public static MachineNamingValidationResult Validate(MachineNamingSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (!settings.IsEnabled)
        {
            return new MachineNamingValidationResult([], 0);
        }

        var issues = new List<MachineNamingValidationIssue>();
        ValidateGlobalSettings(settings, issues);
        if (!Enum.IsDefined(settings.Mode))
        {
            return new MachineNamingValidationResult(issues, 0);
        }

        if (settings.Mode == MachineNamingMode.Manual)
        {
            if (!string.IsNullOrWhiteSpace(settings.ManualInitialValue) &&
                !ComputerNameRules.IsValid(settings.ManualInitialValue))
            {
                issues.Add(new MachineNamingValidationIssue(MachineNamingValidationCode.InvalidManualInitialValue));
            }

            return new MachineNamingValidationResult(issues, 0);
        }

        IReadOnlyList<MachineNameComponentSettings> components = settings.Components ?? [];
        if (components.Count == 0)
        {
            issues.Add(new MachineNamingValidationIssue(MachineNamingValidationCode.ComponentsRequired));
            return new MachineNamingValidationResult(issues, 0);
        }

        var seenTypes = new HashSet<MachineNameComponentType>();
        for (int index = 0; index < components.Count; index++)
        {
            MachineNameComponentSettings component = components[index];
            if (!Enum.IsDefined(component.Type))
            {
                issues.Add(new MachineNamingValidationIssue(MachineNamingValidationCode.UnsupportedComponentType, index));
                continue;
            }

            if (!seenTypes.Add(component.Type))
            {
                issues.Add(new MachineNamingValidationIssue(MachineNamingValidationCode.DuplicateComponentType, index));
            }

            ValidateComponent(component, index, issues);
        }

        int maximumLength = CalculateMaximumLength(components, settings.Separator);
        if (maximumLength > ComputerNameRules.MaxLength)
        {
            issues.Add(new MachineNamingValidationIssue(MachineNamingValidationCode.CharacterBudgetExceeded));
        }

        return new MachineNamingValidationResult(issues, maximumLength);
    }

    public static int CalculateMaximumLength(
        IReadOnlyList<MachineNameComponentSettings> components,
        MachineNameSeparator separator)
    {
        ArgumentNullException.ThrowIfNull(components);
        int length = 0;
        foreach (MachineNameComponentSettings component in components)
        {
            length += component.Type == MachineNameComponentType.StaticText
                ? ComputerNameRules.Sanitize(component.StaticText).Length
                : component.MaximumLength.GetValueOrDefault();
        }

        if (separator == MachineNameSeparator.Hyphen && components.Count > 1)
        {
            length += components.Count - 1;
        }

        return length;
    }

    public static void ThrowIfInvalid(MachineNamingSettings settings)
    {
        MachineNamingValidationResult result = Validate(settings);
        if (!result.IsValid)
        {
            throw new InvalidOperationException("Machine naming configuration is invalid.");
        }
    }

    private static void ValidateGlobalSettings(
        MachineNamingSettings settings,
        ICollection<MachineNamingValidationIssue> issues)
    {
        if (!Enum.IsDefined(settings.Mode))
        {
            issues.Add(new MachineNamingValidationIssue(MachineNamingValidationCode.UnsupportedMode));
        }

        if (!Enum.IsDefined(settings.Separator))
        {
            issues.Add(new MachineNamingValidationIssue(MachineNamingValidationCode.UnsupportedSeparator));
        }

        if (!Enum.IsDefined(settings.Casing))
        {
            issues.Add(new MachineNamingValidationIssue(MachineNamingValidationCode.UnsupportedCasing));
        }
    }

    private static void ValidateComponent(
        MachineNameComponentSettings component,
        int index,
        ICollection<MachineNamingValidationIssue> issues)
    {
        if (component.Type == MachineNameComponentType.StaticText)
        {
            if (string.IsNullOrWhiteSpace(component.StaticText))
            {
                issues.Add(new MachineNamingValidationIssue(MachineNamingValidationCode.StaticTextRequired, index));
            }
            else if (ComputerNameRules.Sanitize(component.StaticText).Length == 0)
            {
                issues.Add(new MachineNamingValidationIssue(MachineNamingValidationCode.StaticTextBecomesEmpty, index));
            }

            AddUnexpected(component.MaximumLength is not null, MachineNamingValidationCode.UnexpectedMaximumLength, index, issues);
            AddUnexpected(component.Truncation is not null, MachineNamingValidationCode.UnexpectedTruncation, index, issues);
            return;
        }

        AddUnexpected(component.StaticText is not null, MachineNamingValidationCode.UnexpectedStaticText, index, issues);
        ValidateMaximumLength(component.MaximumLength, index, issues);

        if (component.Type == MachineNameComponentType.Random)
        {
            AddUnexpected(component.Truncation is not null, MachineNamingValidationCode.UnexpectedTruncation, index, issues);
            return;
        }

        if (component.Truncation is null)
        {
            issues.Add(new MachineNamingValidationIssue(MachineNamingValidationCode.TruncationRequired, index));
        }
        else if (!Enum.IsDefined(component.Truncation.Value))
        {
            issues.Add(new MachineNamingValidationIssue(MachineNamingValidationCode.UnsupportedTruncation, index));
        }
    }

    private static void ValidateMaximumLength(
        int? maximumLength,
        int index,
        ICollection<MachineNamingValidationIssue> issues)
    {
        if (maximumLength is null)
        {
            issues.Add(new MachineNamingValidationIssue(MachineNamingValidationCode.MaximumLengthRequired, index));
        }
        else if (maximumLength is < 1 or > ComputerNameRules.MaxLength)
        {
            issues.Add(new MachineNamingValidationIssue(MachineNamingValidationCode.MaximumLengthOutOfRange, index));
        }
    }

    private static void AddUnexpected(
        bool condition,
        MachineNamingValidationCode code,
        int index,
        ICollection<MachineNamingValidationIssue> issues)
    {
        if (condition)
        {
            issues.Add(new MachineNamingValidationIssue(code, index));
        }
    }
}
