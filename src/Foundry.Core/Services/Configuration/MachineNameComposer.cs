// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.

using Foundry.Core.Models.Configuration;

namespace Foundry.Core.Services.Configuration;

/// <summary>
/// Resolves a deterministic Windows computer name from ordered components.
/// </summary>
public static class MachineNameComposer
{
    public static MachineNameCompositionResult Compose(MachineNameCompositionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var settings = new MachineNamingSettings
        {
            IsEnabled = true,
            Mode = MachineNamingMode.Composed,
            Components = request.Components,
            Separator = request.Separator,
            Casing = request.Casing
        };
        MachineNamingValidationResult validation = MachineNamingValidator.Validate(settings);
        if (!validation.IsValid)
        {
            return new MachineNameCompositionResult
            {
                FailureKind = MachineNameCompositionFailureKind.InvalidConfiguration,
                ConfigurationValidation = validation
            };
        }

        var resolved = new List<string>(request.Components.Count);
        foreach (MachineNameComponentSettings component in request.Components)
        {
            MachineNameCompositionResult? failure = ResolveComponent(component, request, out string value);
            if (failure is not null)
            {
                return failure;
            }

            resolved.Add(value);
        }

        string separator = request.Separator == MachineNameSeparator.Hyphen ? "-" : string.Empty;
        string computerName = string.Join(separator, resolved);
        computerName = request.Casing switch
        {
            MachineNameCasing.Uppercase => computerName.ToUpperInvariant(),
            MachineNameCasing.Lowercase => computerName.ToLowerInvariant(),
            _ => computerName
        };

        return ComputerNameRules.IsValid(computerName)
            ? new MachineNameCompositionResult { ComputerName = computerName }
            : new MachineNameCompositionResult { FailureKind = MachineNameCompositionFailureKind.InvalidFinalName };
    }

    private static MachineNameCompositionResult? ResolveComponent(
        MachineNameComponentSettings component,
        MachineNameCompositionRequest request,
        out string value)
    {
        value = string.Empty;
        string? rawValue;
        if (component.Type == MachineNameComponentType.StaticText)
        {
            rawValue = component.StaticText;
        }
        else if (component.Type == MachineNameComponentType.Random)
        {
            rawValue = request.RandomValue;
            if (rawValue.Length < component.MaximumLength!.Value || !ComputerNameRules.IsAllowedText(rawValue))
            {
                return Failure(MachineNameCompositionFailureKind.InvalidRandomValue, component.Type);
            }
        }
        else
        {
            request.Values.TryGetValue(component.Type, out rawValue);
            if (string.IsNullOrWhiteSpace(rawValue))
            {
                return Failure(MachineNameCompositionFailureKind.MissingHardwareValue, component.Type);
            }

            if (MachineNameHardwareValueRules.IsPlaceholder(rawValue))
            {
                return Failure(MachineNameCompositionFailureKind.PlaceholderHardwareValue, component.Type);
            }
        }

        string sanitized = ComputerNameRules.Sanitize(rawValue);
        if (sanitized.Length == 0)
        {
            return Failure(MachineNameCompositionFailureKind.EmptyAfterSanitization, component.Type);
        }

        if (component.Type == MachineNameComponentType.StaticText)
        {
            value = sanitized;
            return null;
        }

        int length = Math.Min(component.MaximumLength!.Value, sanitized.Length);
        value = component.Type != MachineNameComponentType.Random &&
                component.Truncation == MachineNameTruncation.KeepRight
            ? sanitized[^length..]
            : sanitized[..length];
        return null;
    }

    private static MachineNameCompositionResult Failure(
        MachineNameCompositionFailureKind kind,
        MachineNameComponentType componentType) => new()
    {
        FailureKind = kind,
        ComponentType = componentType
    };
}
