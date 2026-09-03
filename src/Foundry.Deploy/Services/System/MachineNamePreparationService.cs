// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.

using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Configuration;
using Foundry.Deploy.Models;
using Foundry.Deploy.Models.Configuration;

namespace Foundry.Deploy.Services.System;

public static class MachineNamePreparationService
{
    public static MachineNamePreparationResult Prepare(
        DeployMachineNamingSettings settings,
        string fallbackComputerName,
        HardwareProfile? hardware,
        Func<int, string>? randomValueFactory = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        randomValueFactory ??= ComputerNameRandomValueGenerator.Generate;

        if (!settings.IsEnabled)
        {
            return FromManualValue(fallbackComputerName);
        }

        if (settings.Mode == MachineNamingMode.Manual)
        {
            string source = string.IsNullOrWhiteSpace(settings.ManualInitialValue)
                ? fallbackComputerName
                : settings.ManualInitialValue;
            return FromManualValue(source);
        }

        MachineNameComponentSettings? randomComponent = settings.Components
            .FirstOrDefault(component => component.Type == MachineNameComponentType.Random);
        string randomValue = randomComponent?.MaximumLength is int randomLength
            ? randomValueFactory(randomLength)
            : string.Empty;
        var values = new Dictionary<MachineNameComponentType, string?>
        {
            [MachineNameComponentType.SerialNumber] = hardware?.SerialNumber,
            [MachineNameComponentType.Manufacturer] = hardware?.Manufacturer,
            [MachineNameComponentType.Model] = hardware?.Model,
            [MachineNameComponentType.AssetTag] = hardware?.AssetTag,
            [MachineNameComponentType.SystemUuid] = hardware?.SystemUuid
        };
        MachineNameCompositionResult composition = MachineNameComposer.Compose(new MachineNameCompositionRequest
        {
            Components = settings.Components,
            Values = values,
            Separator = settings.Separator,
            Casing = settings.Casing,
            RandomValue = randomValue
        });

        return new MachineNamePreparationResult
        {
            ComputerName = composition.ComputerName,
            FailureKind = composition.FailureKind,
            ComponentType = composition.ComponentType
        };
    }

    private static MachineNamePreparationResult FromManualValue(string? value)
    {
        string computerName = ComputerNameRules.Normalize(value);
        return ComputerNameRules.IsValid(computerName)
            ? new MachineNamePreparationResult { ComputerName = computerName }
            : new MachineNamePreparationResult { FailureKind = MachineNameCompositionFailureKind.InvalidFinalName };
    }
}
