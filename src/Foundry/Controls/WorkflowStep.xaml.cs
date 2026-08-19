// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Microsoft.UI.Xaml.Media;

namespace Foundry.Controls;

/// <summary>
/// Renders a single numbered step within the <see cref="WorkflowStepper"/>.
/// Shows a colored circle with a checkmark, critical icon, or step number.
/// </summary>
public sealed partial class WorkflowStep : UserControl
{
    /// <summary>Occurs when the step is activated.</summary>
    public event RoutedEventHandler? Click;

    /// <summary>Identifies the <see cref="Label"/> dependency property.</summary>
    public static readonly DependencyProperty LabelProperty =
        DependencyProperty.Register(nameof(Label), typeof(string), typeof(WorkflowStep), new PropertyMetadata(string.Empty, OnStateChanged));

    /// <summary>Identifies the <see cref="StepNumber"/> dependency property.</summary>
    public static readonly DependencyProperty StepNumberProperty =
        DependencyProperty.Register(nameof(StepNumber), typeof(string), typeof(WorkflowStep), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty StatusTextProperty =
        DependencyProperty.Register(nameof(StatusText), typeof(string), typeof(WorkflowStep), new PropertyMetadata(string.Empty, OnStateChanged));

    /// <summary>Identifies the <see cref="State"/> dependency property.</summary>
    public static readonly DependencyProperty StateProperty =
        DependencyProperty.Register(nameof(State), typeof(InfoBarSeverity), typeof(WorkflowStep), new PropertyMetadata(InfoBarSeverity.Informational, OnStateChanged));

    /// <summary>
    /// Initializes a new instance of the <see cref="WorkflowStep"/> class.
    /// </summary>
    public WorkflowStep()
    {
        InitializeComponent();
    }

    /// <summary>Gets or sets the label displayed beneath the step circle.</summary>
    public string Label
    {
        get => (string)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    /// <summary>Gets or sets the step number displayed when the step is pending.</summary>
    public string StepNumber
    {
        get => (string)GetValue(StepNumberProperty);
        set => SetValue(StepNumberProperty, value);
    }

    public string StatusText
    {
        get => (string)GetValue(StatusTextProperty);
        set => SetValue(StatusTextProperty, value);
    }

    /// <summary>Gets or sets the current state of this step.</summary>
    public InfoBarSeverity State
    {
        get => (InfoBarSeverity)GetValue(StateProperty);
        set => SetValue(StateProperty, value);
    }

    /// <summary>Gets the background brush for the circle based on current state.</summary>
    public Brush CircleBackground => State switch
    {
        InfoBarSeverity.Success => (Brush)Resources["WorkflowStepReadyBrush"],
        InfoBarSeverity.Error => (Brush)Resources["WorkflowStepCriticalBrush"],
        _ => (Brush)Resources["WorkflowStepPendingBrush"],
    };

    /// <summary>Gets the foreground brush for the circle icon/number based on current state.</summary>
    public Brush CircleForeground => State switch
    {
        InfoBarSeverity.Success => (Brush)Resources["WorkflowStepReadyForegroundBrush"],
        InfoBarSeverity.Error => (Brush)Resources["WorkflowStepCriticalForegroundBrush"],
        _ => (Brush)Resources["WorkflowStepPendingForegroundBrush"],
    };

    /// <summary>Gets the foreground brush for the label text.</summary>
    public Brush LabelForeground => State == InfoBarSeverity.Informational
        ? (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
        : (Brush)Application.Current.Resources["TextFillColorPrimaryBrush"];

    /// <summary>Gets the visibility of the checkmark icon (Ready state only).</summary>
    public Visibility IsReady => State == InfoBarSeverity.Success ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Gets the visibility of the critical icon.</summary>
    public Visibility IsCritical => State == InfoBarSeverity.Error ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Gets the visibility of the step number (Pending state only).</summary>
    public Visibility IsPending => State == InfoBarSeverity.Informational ? Visibility.Visible : Visibility.Collapsed;

    public string AutomationName => string.IsNullOrWhiteSpace(StatusText) ? Label : $"{Label}, {StatusText}";

    private static void OnStateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is WorkflowStep step)
        {
            step.UpdateBindings();
        }
    }

    private void UpdateBindings()
    {
        Bindings.Update();
    }

    private void StepButton_Click(object sender, RoutedEventArgs e)
    {
        Click?.Invoke(this, e);
    }
}
