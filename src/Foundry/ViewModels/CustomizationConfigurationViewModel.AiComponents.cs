using Foundry.Core.Models.Configuration;

namespace Foundry.ViewModels;

public sealed partial class CustomizationConfigurationViewModel
{
    private bool isApplyingAiComponentRemovalSelection;

    public bool IsAiComponentRemovalOptionsEnabled => IsAiComponentRemovalEnabled;

    [ObservableProperty]
    public partial string AiComponentRemovalHeader { get; set; }

    [ObservableProperty]
    public partial string AiComponentRemovalDescription { get; set; }

    [ObservableProperty]
    public partial string AiComponentRemovalEnableText { get; set; }

    [ObservableProperty]
    public partial string AiComponentRemoveCopilotLabel { get; set; }

    [ObservableProperty]
    public partial string AiComponentRemoveCopilotDescription { get; set; }

    [ObservableProperty]
    public partial string AiComponentRemoveAiHubLabel { get; set; }

    [ObservableProperty]
    public partial string AiComponentRemoveAiHubDescription { get; set; }

    [ObservableProperty]
    public partial string AiComponentDisableRecallLabel { get; set; }

    [ObservableProperty]
    public partial string AiComponentDisableRecallDescription { get; set; }

    [ObservableProperty]
    public partial string AiComponentDisableClickToDoLabel { get; set; }

    [ObservableProperty]
    public partial string AiComponentDisableClickToDoDescription { get; set; }

    [ObservableProperty]
    public partial string AiComponentDisableAiServiceAutoStartLabel { get; set; }

    [ObservableProperty]
    public partial string AiComponentDisableAiServiceAutoStartDescription { get; set; }

    [ObservableProperty]
    public partial string AiComponentDisableEdgeAiLabel { get; set; }

    [ObservableProperty]
    public partial string AiComponentDisableEdgeAiDescription { get; set; }

    [ObservableProperty]
    public partial string AiComponentDisablePaintAiLabel { get; set; }

    [ObservableProperty]
    public partial string AiComponentDisablePaintAiDescription { get; set; }

    [ObservableProperty]
    public partial string AiComponentDisableNotepadAiLabel { get; set; }

    [ObservableProperty]
    public partial string AiComponentDisableNotepadAiDescription { get; set; }

    [ObservableProperty]
    public partial bool IsAiComponentRemovalExpanded { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAiComponentRemovalOptionsEnabled))]
    public partial bool IsAiComponentRemovalEnabled { get; set; }

    [ObservableProperty]
    public partial bool RemoveCopilot { get; set; }

    [ObservableProperty]
    public partial bool RemoveAiHub { get; set; }

    [ObservableProperty]
    public partial bool DisableRecall { get; set; }

    [ObservableProperty]
    public partial bool DisableClickToDo { get; set; }

    [ObservableProperty]
    public partial bool DisableAiServiceAutoStart { get; set; }

    [ObservableProperty]
    public partial bool DisableEdgeAi { get; set; }

    [ObservableProperty]
    public partial bool DisablePaintAi { get; set; }

    [ObservableProperty]
    public partial bool DisableNotepadAi { get; set; }

    private void ApplyAiComponentRemovalState(AiComponentRemovalSettings settings)
    {
        bool isEnabled = settings.IsEnabled && HasAnyAiComponentRemovalOptionEnabled(settings);

        IsAiComponentRemovalEnabled = isEnabled;
        IsAiComponentRemovalExpanded = isEnabled;

        isApplyingAiComponentRemovalSelection = true;
        try
        {
            RemoveCopilot = isEnabled && settings.RemoveCopilot;
            RemoveAiHub = isEnabled && settings.RemoveAiHub;
            DisableRecall = isEnabled && settings.DisableRecall;
            DisableClickToDo = isEnabled && settings.DisableClickToDo;
            DisableAiServiceAutoStart = isEnabled && settings.DisableAiServiceAutoStart;
            DisableEdgeAi = isEnabled && settings.DisableEdgeAi;
            DisablePaintAi = isEnabled && settings.DisablePaintAi;
            DisableNotepadAi = isEnabled && settings.DisableNotepadAi;
        }
        finally
        {
            isApplyingAiComponentRemovalSelection = false;
        }
    }

    private AiComponentRemovalSettings BuildAiComponentRemovalSettings()
    {
        var settings = new AiComponentRemovalSettings
        {
            IsEnabled = IsAiComponentRemovalEnabled,
            RemoveCopilot = RemoveCopilot,
            RemoveAiHub = RemoveAiHub,
            DisableRecall = DisableRecall,
            DisableClickToDo = DisableClickToDo,
            DisableAiServiceAutoStart = DisableAiServiceAutoStart,
            DisableEdgeAi = DisableEdgeAi,
            DisablePaintAi = DisablePaintAi,
            DisableNotepadAi = DisableNotepadAi
        };

        return settings.IsEnabled && HasAnyAiComponentRemovalOptionEnabled(settings)
            ? settings
            : new AiComponentRemovalSettings();
    }

    private void RefreshAiComponentRemovalLocalizedText()
    {
        AiComponentRemovalHeader = localizationService.GetString("Customization.AiComponentRemovalHeader");
        AiComponentRemovalDescription = localizationService.GetString("Customization.AiComponentRemovalDescription");
        AiComponentRemovalEnableText = localizationService.GetString("Customization.AiComponentRemovalEnableLabel");
        AiComponentRemoveCopilotLabel = localizationService.GetString("Customization.AiComponentRemoveCopilotLabel");
        AiComponentRemoveCopilotDescription = localizationService.GetString("Customization.AiComponentRemoveCopilotDescription");
        AiComponentRemoveAiHubLabel = localizationService.GetString("Customization.AiComponentRemoveAiHubLabel");
        AiComponentRemoveAiHubDescription = localizationService.GetString("Customization.AiComponentRemoveAiHubDescription");
        AiComponentDisableRecallLabel = localizationService.GetString("Customization.AiComponentDisableRecallLabel");
        AiComponentDisableRecallDescription = localizationService.GetString("Customization.AiComponentDisableRecallDescription");
        AiComponentDisableClickToDoLabel = localizationService.GetString("Customization.AiComponentDisableClickToDoLabel");
        AiComponentDisableClickToDoDescription = localizationService.GetString("Customization.AiComponentDisableClickToDoDescription");
        AiComponentDisableAiServiceAutoStartLabel = localizationService.GetString("Customization.AiComponentDisableAiServiceAutoStartLabel");
        AiComponentDisableAiServiceAutoStartDescription = localizationService.GetString("Customization.AiComponentDisableAiServiceAutoStartDescription");
        AiComponentDisableEdgeAiLabel = localizationService.GetString("Customization.AiComponentDisableEdgeAiLabel");
        AiComponentDisableEdgeAiDescription = localizationService.GetString("Customization.AiComponentDisableEdgeAiDescription");
        AiComponentDisablePaintAiLabel = localizationService.GetString("Customization.AiComponentDisablePaintAiLabel");
        AiComponentDisablePaintAiDescription = localizationService.GetString("Customization.AiComponentDisablePaintAiDescription");
        AiComponentDisableNotepadAiLabel = localizationService.GetString("Customization.AiComponentDisableNotepadAiLabel");
        AiComponentDisableNotepadAiDescription = localizationService.GetString("Customization.AiComponentDisableNotepadAiDescription");
    }

    partial void OnIsAiComponentRemovalEnabledChanged(bool value)
    {
        IsAiComponentRemovalExpanded = value;

        if (isApplyingState || isApplyingAiComponentRemovalSelection)
        {
            return;
        }

        SetAllAiComponentRemovalOptions(value);
        SaveState();
    }

    partial void OnRemoveCopilotChanged(bool value)
    {
        OnAiComponentRemovalOptionChanged();
    }

    partial void OnRemoveAiHubChanged(bool value)
    {
        OnAiComponentRemovalOptionChanged();
    }

    partial void OnDisableRecallChanged(bool value)
    {
        OnAiComponentRemovalOptionChanged();
    }

    partial void OnDisableClickToDoChanged(bool value)
    {
        OnAiComponentRemovalOptionChanged();
    }

    partial void OnDisableAiServiceAutoStartChanged(bool value)
    {
        OnAiComponentRemovalOptionChanged();
    }

    partial void OnDisableEdgeAiChanged(bool value)
    {
        OnAiComponentRemovalOptionChanged();
    }

    partial void OnDisablePaintAiChanged(bool value)
    {
        OnAiComponentRemovalOptionChanged();
    }

    partial void OnDisableNotepadAiChanged(bool value)
    {
        OnAiComponentRemovalOptionChanged();
    }

    private void SetAllAiComponentRemovalOptions(bool value)
    {
        isApplyingAiComponentRemovalSelection = true;
        try
        {
            RemoveCopilot = value;
            RemoveAiHub = value;
            DisableRecall = value;
            DisableClickToDo = value;
            DisableAiServiceAutoStart = value;
            DisableEdgeAi = value;
            DisablePaintAi = value;
            DisableNotepadAi = value;
        }
        finally
        {
            isApplyingAiComponentRemovalSelection = false;
        }
    }

    private void OnAiComponentRemovalOptionChanged()
    {
        if (isApplyingState || isApplyingAiComponentRemovalSelection)
        {
            return;
        }

        bool hasAnyOptionEnabled = HasAnyCurrentAiComponentRemovalOptionEnabled();
        isApplyingAiComponentRemovalSelection = true;
        try
        {
            IsAiComponentRemovalEnabled = hasAnyOptionEnabled;
            IsAiComponentRemovalExpanded = hasAnyOptionEnabled;
        }
        finally
        {
            isApplyingAiComponentRemovalSelection = false;
        }

        SaveState();
    }

    private bool HasAnyCurrentAiComponentRemovalOptionEnabled()
    {
        return RemoveCopilot ||
            RemoveAiHub ||
            DisableRecall ||
            DisableClickToDo ||
            DisableAiServiceAutoStart ||
            DisableEdgeAi ||
            DisablePaintAi ||
            DisableNotepadAi;
    }

    private static bool HasAnyAiComponentRemovalOptionEnabled(AiComponentRemovalSettings settings)
    {
        return settings.RemoveCopilot ||
            settings.RemoveAiHub ||
            settings.DisableRecall ||
            settings.DisableClickToDo ||
            settings.DisableAiServiceAutoStart ||
            settings.DisableEdgeAi ||
            settings.DisablePaintAi ||
            settings.DisableNotepadAi;
    }
}
