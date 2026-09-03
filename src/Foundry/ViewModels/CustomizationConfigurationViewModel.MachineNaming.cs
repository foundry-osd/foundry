// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Configuration;
using Microsoft.UI.Xaml;

namespace Foundry.ViewModels;

public sealed partial class CustomizationConfigurationViewModel
{
    private MachineNamingMode machineNamingMode = MachineNamingMode.Manual;

    public ObservableCollection<MachineNameComponentRowViewModel> MachineNameComponents { get; } = [];

    public ObservableCollection<MachineNameComponentChoice> AvailableMachineNameComponentTypes { get; } = [];

    public ObservableCollection<MachineNameSeparatorChoice> MachineNameSeparatorChoices { get; } = [];

    public ObservableCollection<MachineNameCasingChoice> MachineNameCasingChoices { get; } = [];

    [ObservableProperty]
    public partial bool IsMachineNamingEnabled { get; set; }

    [ObservableProperty]
    public partial string ManualMachineName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool AllowMachineNameEditingDuringDeployment { get; set; } = true;

    [ObservableProperty]
    public partial MachineNameComponentChoice? SelectedAvailableMachineNameComponent { get; set; }

    [ObservableProperty]
    public partial MachineNameSeparatorChoice? SelectedMachineNameSeparator { get; set; }

    [ObservableProperty]
    public partial MachineNameCasingChoice? SelectedMachineNameCasing { get; set; }

    [ObservableProperty]
    public partial string MachineNamingModeLabel { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string MachineNamingModeDescription { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string MachineNamingManualLabel { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string MachineNamingManualDescription { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string MachineNamingComposedLabel { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string MachineNamingComposedDescription { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string MachineNamingComponentsLabel { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string MachineNamingComponentsDescription { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string MachineNamingAddComponentText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string MachineNamingSeparatorLabel { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string MachineNamingCasingLabel { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string MachineNamingEditingLabel { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string MachineNamingEditingDescription { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string MachineNamingPreviewLabel { get; set; } = string.Empty;

    public bool IsManualMachineNamingMode
    {
        get => machineNamingMode == MachineNamingMode.Manual;
        set
        {
            if (value)
            {
                SetMachineNamingMode(MachineNamingMode.Manual);
            }
        }
    }

    public bool IsComposedMachineNamingMode
    {
        get => machineNamingMode == MachineNamingMode.Composed;
        set
        {
            if (value)
            {
                SetMachineNamingMode(MachineNamingMode.Composed);
            }
        }
    }

    public Visibility ManualMachineNamingVisibility => IsManualMachineNamingMode ? Visibility.Visible : Visibility.Collapsed;

    public Visibility ComposedMachineNamingVisibility => IsComposedMachineNamingMode ? Visibility.Visible : Visibility.Collapsed;

    public bool CanAddMachineNameComponent => SelectedAvailableMachineNameComponent is not null
        && MachineNamingRemainingLength > (MachineNameComponents.Count > 0 && SelectedMachineNameSeparator?.Value == MachineNameSeparator.Hyphen ? 1 : 0);

    private int MachineNamingMaximumLength => MachineNamingValidator.CalculateMaximumLength(
        MachineNameComponents.Select(row => row.BuildSettings()).ToArray(),
        SelectedMachineNameSeparator?.Value ?? MachineNameSeparator.None);

    private int MachineNamingRemainingLength => Math.Max(0, ComputerNameRules.MaxLength - MachineNamingMaximumLength);

    public string MachineNamingBudgetText => localizationService.FormatString(
        "Customization.MachineNamingBudgetFormat",
        MachineNamingMaximumLength,
        ComputerNameRules.MaxLength,
        MachineNamingRemainingLength);

    public string MachineNamingPreview
    {
        get
        {
            if (machineNamingMode == MachineNamingMode.Manual)
            {
                return string.IsNullOrWhiteSpace(ManualMachineName)
                    ? localizationService.GetString("Customization.MachineNamingPreviewAtDeployment")
                    : ManualMachineName;
            }

            MachineNameCompositionResult result = MachineNameComposer.Compose(new MachineNameCompositionRequest
            {
                Components = MachineNameComponents.Select(row => row.BuildSettings()).ToArray(),
                Values = new Dictionary<MachineNameComponentType, string?>
                {
                    [MachineNameComponentType.SerialNumber] = "SN1234567890",
                    [MachineNameComponentType.Manufacturer] = "Contoso",
                    [MachineNameComponentType.Model] = "Model123",
                    [MachineNameComponentType.AssetTag] = "ASSET123",
                    [MachineNameComponentType.SystemUuid] = "550E8400E29B41D4A716446655440000"
                },
                Separator = SelectedMachineNameSeparator?.Value ?? MachineNameSeparator.None,
                Casing = SelectedMachineNameCasing?.Value ?? MachineNameCasing.Preserve,
                RandomValue = "ABC123XYZ789012"
            });
            return result.ComputerName ?? localizationService.GetString("Customization.MachineNamingPreviewUnavailable");
        }
    }

    public bool HasMachineNamingValidationError => !string.IsNullOrWhiteSpace(MachineNamingValidationMessage);

    public Visibility MachineNamingValidationVisibility => HasMachineNamingValidationError
        ? Visibility.Visible
        : Visibility.Collapsed;

    public string MachineNamingValidationMessage => !IsMachineNamingEnabled || MachineNamingValidator.Validate(BuildMachineNamingSettings()).IsValid
        ? string.Empty
        : localizationService.GetString("Customization.MachineNamingValidation");

    public void AddMachineNameComponent()
    {
        if (SelectedAvailableMachineNameComponent is not { } choice)
        {
            return;
        }

        int separatorCost = MachineNameComponents.Count > 0 && SelectedMachineNameSeparator?.Value == MachineNameSeparator.Hyphen ? 1 : 0;
        int availableLength = Math.Max(0, MachineNamingRemainingLength - separatorCost);
        if (availableLength == 0)
        {
            return;
        }

        int defaultLength = Math.Min(choice.Type == MachineNameComponentType.Random ? 6 : 15, availableLength);
        string staticText = "PC"[..Math.Min(2, availableLength)];
        MachineNameComponentSettings settings = choice.Type switch
        {
            MachineNameComponentType.StaticText => new MachineNameComponentSettings { Type = choice.Type, StaticText = staticText },
            MachineNameComponentType.Random => new MachineNameComponentSettings { Type = choice.Type, MaximumLength = defaultLength },
            MachineNameComponentType.SerialNumber => new MachineNameComponentSettings
            {
                Type = choice.Type,
                MaximumLength = defaultLength,
                Truncation = MachineNameTruncation.KeepRight
            },
            _ => new MachineNameComponentSettings
            {
                Type = choice.Type,
                MaximumLength = defaultLength,
                Truncation = MachineNameTruncation.KeepLeft
            }
        };
        MachineNameComponents.Add(CreateMachineNameComponentRow(settings));
        OnMachineNameComponentsChanged();
    }

    public void RemoveMachineNameComponent(MachineNameComponentRowViewModel row)
    {
        if (MachineNameComponents.Remove(row))
        {
            OnMachineNameComponentsChanged();
        }
    }

    public void MoveMachineNameComponent(MachineNameComponentRowViewModel row, int offset)
    {
        int currentIndex = MachineNameComponents.IndexOf(row);
        int targetIndex = currentIndex + offset;
        if (currentIndex >= 0 && targetIndex >= 0 && targetIndex < MachineNameComponents.Count)
        {
            MachineNameComponents.Move(currentIndex, targetIndex);
            OnMachineNameComponentsChanged();
        }
    }

    partial void OnIsMachineNamingEnabledChanged(bool value)
    {
        RaiseMachineNamingPropertiesChanged();
        SaveState();
    }

    partial void OnManualMachineNameChanged(string value)
    {
        if (!isApplyingState)
        {
            string normalized = ComputerNameRules.Normalize(value);
            if (!string.Equals(value, normalized, StringComparison.Ordinal))
            {
                ManualMachineName = normalized;
                return;
            }
        }

        RaiseMachineNamingPropertiesChanged();
        SaveState();
    }

    partial void OnAllowMachineNameEditingDuringDeploymentChanged(bool value) => SaveState();

    partial void OnSelectedAvailableMachineNameComponentChanged(MachineNameComponentChoice? value) =>
        OnPropertyChanged(nameof(CanAddMachineNameComponent));

    partial void OnSelectedMachineNameSeparatorChanged(MachineNameSeparatorChoice? value)
    {
        RaiseMachineNamingPropertiesChanged();
        SaveState();
    }

    partial void OnSelectedMachineNameCasingChanged(MachineNameCasingChoice? value)
    {
        RaiseMachineNamingPropertiesChanged();
        SaveState();
    }

    private void ApplyMachineNamingState(MachineNamingSettings settings)
    {
        IsMachineNamingEnabled = settings.IsEnabled;
        machineNamingMode = settings.Mode;
        ManualMachineName = settings.ManualInitialValue ?? string.Empty;
        AllowMachineNameEditingDuringDeployment = settings.AllowEditingDuringDeployment;
        MachineNameComponents.Clear();
        foreach (MachineNameComponentSettings component in settings.Components)
        {
            MachineNameComponents.Add(CreateMachineNameComponentRow(component));
        }

        EnsureMachineNamingChoices();
        SelectedMachineNameSeparator = MachineNameSeparatorChoices.FirstOrDefault(choice => choice.Value == settings.Separator)
            ?? MachineNameSeparatorChoices[0];
        SelectedMachineNameCasing = MachineNameCasingChoices.FirstOrDefault(choice => choice.Value == settings.Casing)
            ?? MachineNameCasingChoices[0];
        RefreshAvailableMachineNameComponentTypes();
        RaiseMachineNamingPropertiesChanged();
    }

    private MachineNamingSettings BuildMachineNamingSettings() => new()
    {
        IsEnabled = IsMachineNamingEnabled,
        Mode = machineNamingMode,
        ManualInitialValue = machineNamingMode == MachineNamingMode.Manual && !string.IsNullOrWhiteSpace(ManualMachineName)
            ? ManualMachineName
            : null,
        Components = machineNamingMode == MachineNamingMode.Composed
            ? MachineNameComponents.Select(row => row.BuildSettings()).ToArray()
            : [],
        Separator = SelectedMachineNameSeparator?.Value ?? MachineNameSeparator.None,
        Casing = SelectedMachineNameCasing?.Value ?? MachineNameCasing.Preserve,
        AllowEditingDuringDeployment = AllowMachineNameEditingDuringDeployment
    };

    private void SetMachineNamingMode(MachineNamingMode mode)
    {
        if (machineNamingMode == mode)
        {
            return;
        }

        machineNamingMode = mode;
        if (mode == MachineNamingMode.Composed && MachineNameComponents.Count == 0)
        {
            MachineNameComponents.Add(CreateMachineNameComponentRow(new MachineNameComponentSettings
            {
                Type = MachineNameComponentType.SerialNumber,
                MaximumLength = 15,
                Truncation = MachineNameTruncation.KeepRight
            }));
            RefreshAvailableMachineNameComponentTypes();
        }

        RaiseMachineNamingPropertiesChanged();
        SaveState();
    }

    private void OnMachineNameComponentsChanged()
    {
        RefreshAvailableMachineNameComponentTypes();
        RaiseMachineNamingPropertiesChanged();
        SaveState();
    }

    private MachineNameComponentRowViewModel CreateMachineNameComponentRow(MachineNameComponentSettings settings) =>
        new(settings, GetMachineNameComponentDisplayName(settings.Type), OnMachineNameComponentsChanged);

    private void EnsureMachineNamingChoices()
    {
        MachineNameSeparator valueSeparator = SelectedMachineNameSeparator?.Value ?? MachineNameSeparator.None;
        MachineNameCasing valueCasing = SelectedMachineNameCasing?.Value ?? MachineNameCasing.Preserve;
        MachineNameSeparatorChoices.Clear();
        MachineNameSeparatorChoices.Add(new MachineNameSeparatorChoice(MachineNameSeparator.None, localizationService.GetString("Customization.MachineNamingSeparatorNone")));
        MachineNameSeparatorChoices.Add(new MachineNameSeparatorChoice(MachineNameSeparator.Hyphen, localizationService.GetString("Customization.MachineNamingSeparatorHyphen")));
        MachineNameCasingChoices.Clear();
        MachineNameCasingChoices.Add(new MachineNameCasingChoice(MachineNameCasing.Preserve, localizationService.GetString("Customization.MachineNamingCasingPreserve")));
        MachineNameCasingChoices.Add(new MachineNameCasingChoice(MachineNameCasing.Uppercase, localizationService.GetString("Customization.MachineNamingCasingUppercase")));
        MachineNameCasingChoices.Add(new MachineNameCasingChoice(MachineNameCasing.Lowercase, localizationService.GetString("Customization.MachineNamingCasingLowercase")));
        SelectedMachineNameSeparator = MachineNameSeparatorChoices.FirstOrDefault(choice => choice.Value == valueSeparator)
            ?? MachineNameSeparatorChoices[0];
        SelectedMachineNameCasing = MachineNameCasingChoices.FirstOrDefault(choice => choice.Value == valueCasing)
            ?? MachineNameCasingChoices[0];
    }

    private void RefreshAvailableMachineNameComponentTypes()
    {
        MachineNameComponentType? selectedType = SelectedAvailableMachineNameComponent?.Type;
        HashSet<MachineNameComponentType> usedTypes = MachineNameComponents.Select(row => row.Type).ToHashSet();
        AvailableMachineNameComponentTypes.Clear();
        foreach (MachineNameComponentType type in Enum.GetValues<MachineNameComponentType>().Where(type => !usedTypes.Contains(type)))
        {
            AvailableMachineNameComponentTypes.Add(new MachineNameComponentChoice(type, GetMachineNameComponentDisplayName(type)));
        }

        SelectedAvailableMachineNameComponent = AvailableMachineNameComponentTypes.FirstOrDefault(choice => choice.Type == selectedType)
            ?? AvailableMachineNameComponentTypes.FirstOrDefault();
    }

    private void RefreshMachineNamingLocalizedText()
    {
        MachineNamingModeLabel = localizationService.GetString("Customization.MachineNamingModeLabel");
        MachineNamingModeDescription = localizationService.GetString("Customization.MachineNamingModeDescription");
        MachineNamingManualLabel = localizationService.GetString("Customization.MachineNamingManualLabel");
        MachineNamingManualDescription = localizationService.GetString("Customization.MachineNamingManualDescription");
        MachineNamingComposedLabel = localizationService.GetString("Customization.MachineNamingComposedLabel");
        MachineNamingComposedDescription = localizationService.GetString("Customization.MachineNamingComposedDescription");
        MachineNamingComponentsLabel = localizationService.GetString("Customization.MachineNamingComponentsLabel");
        MachineNamingComponentsDescription = localizationService.GetString("Customization.MachineNamingComponentsDescription");
        MachineNamingAddComponentText = localizationService.GetString("Customization.MachineNamingAddComponent");
        MachineNamingSeparatorLabel = localizationService.GetString("Customization.MachineNamingSeparatorLabel");
        MachineNamingCasingLabel = localizationService.GetString("Customization.MachineNamingCasingLabel");
        MachineNamingEditingLabel = localizationService.GetString("Customization.MachineNamingEditingLabel");
        MachineNamingEditingDescription = localizationService.GetString("Customization.MachineNamingEditingDescription");
        MachineNamingPreviewLabel = localizationService.GetString("MachineNamingPreviewCard.Header");
        EnsureMachineNamingChoices();
        foreach (MachineNameComponentRowViewModel row in MachineNameComponents)
        {
            row.RefreshDisplayName(GetMachineNameComponentDisplayName(row.Type));
        }

        RefreshAvailableMachineNameComponentTypes();
    }

    private string GetMachineNameComponentDisplayName(MachineNameComponentType type) => localizationService.GetString(
        type switch
        {
            MachineNameComponentType.StaticText => "Customization.MachineNamingComponentStaticText",
            MachineNameComponentType.SerialNumber => "Customization.MachineNamingComponentSerialNumber",
            MachineNameComponentType.Manufacturer => "Customization.MachineNamingComponentManufacturer",
            MachineNameComponentType.Model => "Customization.MachineNamingComponentModel",
            MachineNameComponentType.AssetTag => "Customization.MachineNamingComponentAssetTag",
            MachineNameComponentType.SystemUuid => "Customization.MachineNamingComponentSystemUuid",
            MachineNameComponentType.Random => "Customization.MachineNamingComponentRandom",
            _ => "Common.Unavailable"
        });

    private void RaiseMachineNamingPropertiesChanged()
    {
        MachineNamingValidationResult validation = MachineNamingValidator.Validate(BuildMachineNamingSettings());
        string componentValidation = localizationService.GetString("Customization.MachineNamingValidation");
        for (int index = 0; index < MachineNameComponents.Count; index++)
        {
            bool hasIssue = validation.Issues.Any(issue => issue.ComponentIndex == index);
            MachineNameComponents[index].SetValidationMessage(hasIssue ? componentValidation : string.Empty);
            MachineNameComponents[index].UpdatePosition(index, MachineNameComponents.Count);
        }

        OnPropertyChanged(nameof(IsManualMachineNamingMode));
        OnPropertyChanged(nameof(IsComposedMachineNamingMode));
        OnPropertyChanged(nameof(ManualMachineNamingVisibility));
        OnPropertyChanged(nameof(ComposedMachineNamingVisibility));
        OnPropertyChanged(nameof(CanAddMachineNameComponent));
        OnPropertyChanged(nameof(MachineNamingBudgetText));
        OnPropertyChanged(nameof(MachineNamingPreview));
        OnPropertyChanged(nameof(MachineNamingValidationMessage));
        OnPropertyChanged(nameof(HasMachineNamingValidationError));
        OnPropertyChanged(nameof(MachineNamingValidationVisibility));
    }
}
