// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;

namespace Foundry.Avalonia.Controls;

[PseudoClasses(":neutral", ":success", ":caution", ":critical", ":has-icon", ":has-content")]
public class StatusIndicator : ContentControl
{
    public static readonly StyledProperty<StatusIndicatorKind> KindProperty =
        AvaloniaProperty.Register<StatusIndicator, StatusIndicatorKind>(nameof(Kind));

    public static readonly StyledProperty<object?> IconProperty =
        AvaloniaProperty.Register<StatusIndicator, object?>(nameof(Icon));

    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<StatusIndicator, string?>(nameof(Title));

    public static readonly StyledProperty<string?> DescriptionProperty =
        AvaloniaProperty.Register<StatusIndicator, string?>(nameof(Description));

    private string? _generatedAutomationName;
    private string? _generatedAutomationHelpText;

    public StatusIndicator()
    {
        AutomationProperties.SetLiveSetting(this, AutomationLiveSetting.Polite);
        UpdateKindPseudoClasses(Kind);
        UpdateSlotPseudoClasses();
    }

    public StatusIndicatorKind Kind
    {
        get => GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    public object? Icon
    {
        get => GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string? Description
    {
        get => GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == KindProperty)
        {
            UpdateKindPseudoClasses(change.GetNewValue<StatusIndicatorKind>());
        }

        if (change.Property == IconProperty || change.Property == ContentProperty)
        {
            UpdateSlotPseudoClasses();
        }

        if (change.Property == TitleProperty || change.Property == DescriptionProperty)
        {
            UpdateGeneratedAutomationText();
        }
    }

    private void UpdateKindPseudoClasses(StatusIndicatorKind kind)
    {
        PseudoClasses.Set(":neutral", kind == StatusIndicatorKind.Neutral);
        PseudoClasses.Set(":success", kind == StatusIndicatorKind.Success);
        PseudoClasses.Set(":caution", kind == StatusIndicatorKind.Caution);
        PseudoClasses.Set(":critical", kind == StatusIndicatorKind.Critical);
    }

    private void UpdateSlotPseudoClasses()
    {
        PseudoClasses.Set(":has-icon", Icon is not null);
        PseudoClasses.Set(":has-content", Content is not null);
    }

    private void UpdateGeneratedAutomationText()
    {
        string? currentName = AutomationProperties.GetName(this);
        if (currentName is null || currentName == _generatedAutomationName)
        {
            _generatedAutomationName = Title;
            AutomationProperties.SetName(this, _generatedAutomationName);
        }

        string? currentHelpText = AutomationProperties.GetHelpText(this);
        if (currentHelpText is null || currentHelpText == _generatedAutomationHelpText)
        {
            _generatedAutomationHelpText = Description;
            AutomationProperties.SetHelpText(this, _generatedAutomationHelpText);
        }
    }
}
