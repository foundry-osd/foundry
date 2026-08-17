// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Controls;

/// <summary>
/// Displays the three-step ADK → Configuration → Create media workflow
/// with colored step indicators and navigation affordances.
/// </summary>
public sealed partial class WorkflowStepper : UserControl
{
    /// <summary>Identifies the <see cref="Step1Label"/> dependency property.</summary>
    public static readonly DependencyProperty Step1LabelProperty =
        DependencyProperty.Register(nameof(Step1Label), typeof(string), typeof(WorkflowStepper), new PropertyMetadata(string.Empty));

    /// <summary>Identifies the <see cref="Step2Label"/> dependency property.</summary>
    public static readonly DependencyProperty Step2LabelProperty =
        DependencyProperty.Register(nameof(Step2Label), typeof(string), typeof(WorkflowStepper), new PropertyMetadata(string.Empty));

    /// <summary>Identifies the <see cref="Step3Label"/> dependency property.</summary>
    public static readonly DependencyProperty Step3LabelProperty =
        DependencyProperty.Register(nameof(Step3Label), typeof(string), typeof(WorkflowStepper), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty Step1StatusTextProperty =
        DependencyProperty.Register(nameof(Step1StatusText), typeof(string), typeof(WorkflowStepper), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty Step2StatusTextProperty =
        DependencyProperty.Register(nameof(Step2StatusText), typeof(string), typeof(WorkflowStepper), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty Step3StatusTextProperty =
        DependencyProperty.Register(nameof(Step3StatusText), typeof(string), typeof(WorkflowStepper), new PropertyMetadata(string.Empty));

    /// <summary>Identifies the <see cref="Step1State"/> dependency property.</summary>
    public static readonly DependencyProperty Step1StateProperty =
        DependencyProperty.Register(nameof(Step1State), typeof(InfoBarSeverity), typeof(WorkflowStepper), new PropertyMetadata(InfoBarSeverity.Informational));

    /// <summary>Identifies the <see cref="Step2State"/> dependency property.</summary>
    public static readonly DependencyProperty Step2StateProperty =
        DependencyProperty.Register(nameof(Step2State), typeof(InfoBarSeverity), typeof(WorkflowStepper), new PropertyMetadata(InfoBarSeverity.Informational));

    /// <summary>Identifies the <see cref="Step3State"/> dependency property.</summary>
    public static readonly DependencyProperty Step3StateProperty =
        DependencyProperty.Register(nameof(Step3State), typeof(InfoBarSeverity), typeof(WorkflowStepper), new PropertyMetadata(InfoBarSeverity.Informational));

    public static readonly DependencyProperty IsPostAdkNavigationEnabledProperty =
        DependencyProperty.Register(nameof(IsPostAdkNavigationEnabled), typeof(bool), typeof(WorkflowStepper), new PropertyMetadata(true));

    /// <summary>Raised when step 1 is activated.</summary>
    public event EventHandler? Step1Requested;

    /// <summary>Raised when step 2 is activated.</summary>
    public event EventHandler? Step2Requested;

    /// <summary>Raised when step 3 is activated.</summary>
    public event EventHandler? Step3Requested;

    /// <summary>
    /// Initializes a new instance of the <see cref="WorkflowStepper"/> class.
    /// </summary>
    public WorkflowStepper()
    {
        InitializeComponent();
    }

    /// <summary>Gets or sets the label for step 1.</summary>
    public string Step1Label
    {
        get => (string)GetValue(Step1LabelProperty);
        set => SetValue(Step1LabelProperty, value);
    }

    /// <summary>Gets or sets the label for step 2.</summary>
    public string Step2Label
    {
        get => (string)GetValue(Step2LabelProperty);
        set => SetValue(Step2LabelProperty, value);
    }

    /// <summary>Gets or sets the label for step 3.</summary>
    public string Step3Label
    {
        get => (string)GetValue(Step3LabelProperty);
        set => SetValue(Step3LabelProperty, value);
    }

    public string Step1StatusText
    {
        get => (string)GetValue(Step1StatusTextProperty);
        set => SetValue(Step1StatusTextProperty, value);
    }

    public string Step2StatusText
    {
        get => (string)GetValue(Step2StatusTextProperty);
        set => SetValue(Step2StatusTextProperty, value);
    }

    public string Step3StatusText
    {
        get => (string)GetValue(Step3StatusTextProperty);
        set => SetValue(Step3StatusTextProperty, value);
    }

    /// <summary>Gets or sets the state of step 1.</summary>
    public InfoBarSeverity Step1State
    {
        get => (InfoBarSeverity)GetValue(Step1StateProperty);
        set => SetValue(Step1StateProperty, value);
    }

    /// <summary>Gets or sets the state of step 2.</summary>
    public InfoBarSeverity Step2State
    {
        get => (InfoBarSeverity)GetValue(Step2StateProperty);
        set => SetValue(Step2StateProperty, value);
    }

    /// <summary>Gets or sets the state of step 3.</summary>
    public InfoBarSeverity Step3State
    {
        get => (InfoBarSeverity)GetValue(Step3StateProperty);
        set => SetValue(Step3StateProperty, value);
    }

    public bool IsPostAdkNavigationEnabled
    {
        get => (bool)GetValue(IsPostAdkNavigationEnabledProperty);
        set => SetValue(IsPostAdkNavigationEnabledProperty, value);
    }

    private void Step1_Click(object sender, RoutedEventArgs e) => Step1Requested?.Invoke(this, EventArgs.Empty);

    private void Step2_Click(object sender, RoutedEventArgs e) => Step2Requested?.Invoke(this, EventArgs.Empty);

    private void Step3_Click(object sender, RoutedEventArgs e) => Step3Requested?.Invoke(this, EventArgs.Empty);
}
