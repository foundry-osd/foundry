// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Foundry.Deploy.Controls;
using Foundry.Deploy.Services.Wizard;
using Foundry.Deploy.ViewModels;
using Foundry.Deploy.Views;
using Foundry.Deploy.Views.Wizard;

namespace Foundry.Deploy.Tests;

public sealed class XamlResourceLoadingTests
{
    [Fact]
    public void ApplicationResources_LoadViewsAndProvideExpectedStylesAndLayout()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var application = new App();
                application.InitializeComponent();
                var wizardView = new WizardView();
                var wizardContentCard = Assert.IsType<Border>(wizardView.FindName("WizardContentCard"));
                var wizardFooter = Assert.IsType<Grid>(wizardView.FindName("WizardFooter"));
                var nextButton = Assert.IsType<Button>(wizardView.FindName("NextButton"));
                var returnToSummaryButton = Assert.IsType<Button>(wizardView.FindName("ReturnToSummaryButton"));
                var deployButton = Assert.IsType<Button>(wizardView.FindName("DeployButton"));
                var stepperColumn = Assert.IsType<ColumnDefinition>(wizardView.FindName("StepperColumn"));
                var stepperGapColumn = Assert.IsType<ColumnDefinition>(wizardView.FindName("StepperGapColumn"));
                Assert.Equal(980, wizardContentCard.MaxWidth);
                Assert.Equal(190, stepperColumn.Width.Value);
                Assert.Equal(16, stepperGapColumn.Width.Value);
                Assert.Equal(wizardContentCard.MaxWidth, wizardFooter.MaxWidth);
                Assert.Equal(HorizontalAlignment.Center, wizardFooter.HorizontalAlignment);
                Assert.Same(application.FindResource(typeof(Button)), nextButton.Style.BasedOn);
                Assert.Same(application.FindResource("AccentButtonStyle"), returnToSummaryButton.Style.BasedOn);
                Assert.Equal(0, nextButton.Margin.Right);
                Assert.Equal(0, returnToSummaryButton.Margin.Right);
                Assert.Equal(0, deployButton.Margin.Right);
                wizardView.Measure(new Size(1280, 800));
                wizardView.Arrange(new Rect(0, 0, 1280, 800));
                wizardView.UpdateLayout();
                Assert.Equal(wizardContentCard.ActualWidth, wizardFooter.ActualWidth);

                var verticalStepTemplate = Assert.IsType<DataTemplate>(
                    wizardView.FindResource("VerticalStepTemplate"));
                var completedStep = new DeploymentWizardStepViewModel(
                    new DeploymentWizardStepDefinition(
                        DeploymentWizardStepId.TargetDevice,
                        "Wizard.Step.TargetDevice"),
                    "Target device",
                    displayNumber: 1,
                    isFirst: true,
                    isLast: false)
                {
                    IsCompleted = true,
                    IsEnabled = true,
                    IsConnectorCompleted = true
                };
                var verticalStep = Assert.IsAssignableFrom<FrameworkElement>(verticalStepTemplate.LoadContent());
                verticalStep.DataContext = completedStep;
                var stepHost = new Grid { Width = 220, Height = 72 };
                stepHost.Children.Add(verticalStep);
                stepHost.Measure(new Size(220, 72));
                stepHost.Arrange(new Rect(0, 0, 220, 72));
                stepHost.UpdateLayout();
                var verticalMarker = Assert.Single(
                    FindVisualDescendants<Border>(verticalStep),
                    border => border.Name == "VerticalStepMarker");
                var verticalCheck = Assert.Single(
                    FindVisualDescendants<TextBlock>(verticalStep),
                    textBlock => textBlock.Name == "VerticalStepCheck");
                var verticalNumber = Assert.Single(
                    FindVisualDescendants<TextBlock>(verticalStep),
                    textBlock => textBlock.Name == "VerticalStepNumber");
                var verticalConnector = Assert.Single(
                    FindVisualDescendants<Border>(verticalStep),
                    border => border.Name == "VerticalStepConnector");
                var verticalButtonSurface = Assert.Single(
                    FindVisualDescendants<Border>(verticalStep),
                    border => border.Name == "ButtonSurface");
                var verticalStepButton = Assert.Single(
                    FindVisualDescendants<Button>(verticalStep),
                    button => button.Name == "VerticalStepButton");
                Assert.Equal(32, verticalMarker.ActualWidth);
                Assert.Equal(32, verticalMarker.ActualHeight);
                Assert.Equal(Visibility.Visible, verticalCheck.Visibility);
                Assert.Equal(Visibility.Collapsed, verticalNumber.Visibility);
                Assert.Equal(2, verticalConnector.ActualWidth);
                Assert.Equal(18, verticalConnector.ActualHeight);
                Assert.Same(application.FindResource("SystemFillColorSuccessBrush"), verticalCheck.Foreground);
                Assert.Same(application.FindResource("SystemFillColorSuccessBrush"), verticalConnector.Background);
                double markerCenter = verticalMarker.TranslatePoint(
                    new Point(0, verticalMarker.ActualHeight / 2),
                    verticalButtonSurface).Y;
                Assert.Equal(verticalButtonSurface.ActualHeight / 2, markerCenter, precision: 3);
                double buttonBottom = verticalStepButton.TranslatePoint(
                    new Point(0, verticalStepButton.ActualHeight),
                    verticalStep).Y;
                double connectorTop = verticalConnector.TranslatePoint(new Point(), verticalStep).Y;
                Assert.Equal(verticalButtonSurface.ActualHeight, verticalStepButton.ActualHeight, precision: 3);
                Assert.True(
                    buttonBottom <= connectorTop,
                    $"The button extends to {buttonBottom}px and overlaps the connector starting at {connectorTop}px.");

                var disabledVerticalStep = new DeploymentWizardStepViewModel(
                    new DeploymentWizardStepDefinition(
                        DeploymentWizardStepId.Drivers,
                        "Wizard.Step.Drivers"),
                    "Drivers",
                    displayNumber: 3,
                    isFirst: false,
                    isLast: false)
                {
                    IsEnabled = false,
                };
                var disabledVerticalRoot = Assert.IsAssignableFrom<FrameworkElement>(
                    verticalStepTemplate.LoadContent());
                disabledVerticalRoot.DataContext = disabledVerticalStep;
                disabledVerticalRoot.Measure(new Size(220, 72));
                disabledVerticalRoot.Arrange(new Rect(0, 0, 220, 72));
                disabledVerticalRoot.UpdateLayout();
                var disabledVerticalButton = Assert.Single(
                    FindVisualDescendants<Button>(disabledVerticalRoot),
                    button => button.Name == "VerticalStepButton");
                Assert.False(disabledVerticalButton.IsEnabled);

                var compactStepTemplate = Assert.IsType<DataTemplate>(
                    wizardView.FindResource("CompactStepTemplate"));
                var currentStep = new DeploymentWizardStepViewModel(
                    new DeploymentWizardStepDefinition(
                        DeploymentWizardStepId.OperatingSystem,
                        "Wizard.Step.OperatingSystem"),
                    "Operating system",
                    displayNumber: 2,
                    isFirst: false,
                    isLast: false)
                {
                    IsCurrent = true,
                    IsConnectorCompleted = true,
                    IsPreviousConnectorCompleted = true
                };
                var compactStep = Assert.IsType<Button>(compactStepTemplate.LoadContent());
                compactStep.DataContext = currentStep;
                var compactHost = new Grid { Width = 180, Height = 76 };
                compactHost.Children.Add(compactStep);
                compactHost.Measure(new Size(180, 76));
                compactHost.Arrange(new Rect(0, 0, 180, 76));
                compactHost.UpdateLayout();
                var compactMarker = Assert.Single(
                    FindVisualDescendants<Border>(compactStep),
                    border => border.Name == "CompactStepMarker");
                var compactNumber = Assert.Single(
                    FindVisualDescendants<TextBlock>(compactStep),
                    textBlock => textBlock.Name == "CompactStepNumber");
                var compactLeftConnector = Assert.Single(
                    FindVisualDescendants<Border>(compactStep),
                    border => border.Name == "CompactStepLeftConnector");
                var compactRightConnector = Assert.Single(
                    FindVisualDescendants<Border>(compactStep),
                    border => border.Name == "CompactStepRightConnector");
                var compactButtonSurface = Assert.Single(
                    FindVisualDescendants<Border>(compactStep),
                    border => border.Name == "ButtonSurface");
                Assert.Equal(32, compactMarker.ActualWidth);
                Assert.Equal(32, compactMarker.ActualHeight);
                Assert.Equal("2", compactNumber.Text);
                Assert.Equal(2, compactLeftConnector.ActualHeight);
                Assert.Equal(2, compactRightConnector.ActualHeight);
                Assert.Same(application.FindResource("AccentFillColorDefaultBrush"), compactMarker.Background);
                Assert.Same(
                    application.FindResource("SystemFillColorSuccessBrush"),
                    compactRightConnector.Background);
                Assert.Equal(
                    Colors.Transparent,
                    Assert.IsType<SolidColorBrush>(compactButtonSurface.Background).Color);

                var disabledCompactStep = new DeploymentWizardStepViewModel(
                    new DeploymentWizardStepDefinition(
                        DeploymentWizardStepId.Summary,
                        "Wizard.Step.Summary"),
                    "Summary",
                    displayNumber: 4,
                    isFirst: false,
                    isLast: true)
                {
                    IsEnabled = false,
                    IsPreviousConnectorCompleted = true
                };
                var disabledCompactButton = Assert.IsType<Button>(compactStepTemplate.LoadContent());
                disabledCompactButton.DataContext = disabledCompactStep;
                disabledCompactButton.Measure(new Size(180, 76));
                disabledCompactButton.Arrange(new Rect(0, 0, 180, 76));
                disabledCompactButton.UpdateLayout();
                Assert.False(disabledCompactButton.IsEnabled);

                var deploymentStatusView = new DeploymentStatusView();
                var stepsRail = Assert.IsType<Grid>(deploymentStatusView.FindName("StepsRail"));
                Assert.Equal(VerticalAlignment.Center, stepsRail.VerticalAlignment);
                Assert.Equal(720, stepsRail.MaxHeight);

                var summaryStepView = new SummaryStepView();
                var summaryRoot = Assert.IsType<StackPanel>(summaryStepView.Content);
                var summaryCategories = Assert.Single(summaryRoot.Children.OfType<ItemsControl>());
                var summaryCategoryExpander = Assert.IsType<Expander>(summaryCategories.ItemTemplate.LoadContent());
                summaryCategoryExpander.DataContext = new DeploymentSummaryCategoryViewModel(
                    "Target device",
                    "PC-001",
                    DeploymentSummaryStatus.Configured,
                    [],
                    DeploymentWizardStepId.TargetDevice);
                var summaryHeader = Assert.IsType<Grid>(summaryCategoryExpander.Header);
                var editButton = Assert.Single(summaryHeader.Children.OfType<Button>());
                var summaryHost = new Grid { Width = 800, Height = 80 };
                summaryHost.Children.Add(summaryCategoryExpander);
                summaryHost.Measure(new Size(800, 80));
                summaryHost.Arrange(new Rect(0, 0, 800, 80));
                summaryHost.UpdateLayout();
                var chevron = Assert.Single(
                    FindVisualDescendants<TextBlock>(summaryCategoryExpander),
                    textBlock => textBlock.Name == "ControlChevronIcon");
                double editButtonRight = editButton.TranslatePoint(
                    new Point(editButton.ActualWidth, 0),
                    summaryCategoryExpander).X;
                double chevronLeft = chevron.TranslatePoint(new Point(0, 0), summaryCategoryExpander).X;
                Assert.True(chevronLeft - editButtonRight >= 12);

                var progressBarStyle = Assert.IsType<Style>(application.FindResource("DeployProgressBarStyle"));
                Setter effectSetter = Assert.Single(
                    progressBarStyle.Setters.OfType<Setter>(),
                    setter => setter.Property == UIElement.EffectProperty);
                var progressEffect = Assert.IsType<DropShadowEffect>(effectSetter.Value);
                Assert.Equal(Color.FromRgb(0, 183, 255), progressEffect.Color);
                Assert.Equal(20, progressEffect.BlurRadius);
                Assert.Equal(0, progressEffect.ShadowDepth);
                Assert.Equal(1, progressEffect.Opacity);

                var glyph = new TerminalStatusGlyph { IsSuccess = true };
                glyph.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
                var foregroundGlyph = Assert.IsType<TextBlock>(glyph.FindName("ForegroundGlyph"));
                var glyphEffect = Assert.IsType<DropShadowEffect>(foregroundGlyph.Effect);
                var signalBrush = Assert.IsType<SolidColorBrush>(foregroundGlyph.Foreground);
                Assert.Equal(signalBrush.Color, glyphEffect.Color);
                Assert.Equal(16, glyphEffect.BlurRadius);
                Assert.Equal(0, glyphEffect.ShadowDepth);
                Assert.Equal(1, glyphEffect.Opacity);

                var ring = new CustomProgressRing();
                Assert.False(ring.ClipToBounds);
                var determinateArc = Assert.IsType<System.Windows.Shapes.Path>(ring.FindName("DeterminateArc"));
                var indeterminateArc = Assert.IsType<System.Windows.Shapes.Path>(ring.FindName("IndeterminateArc"));
                var track = Assert.IsType<System.Windows.Shapes.Ellipse>(ring.FindName("TrackCircle"));
                var determinateEffect = Assert.IsType<DropShadowEffect>(determinateArc.Effect);
                var indeterminateEffect = Assert.IsType<DropShadowEffect>(indeterminateArc.Effect);
                Assert.Equal(Color.FromRgb(0, 183, 255), determinateEffect.Color);
                Assert.Equal(16, determinateEffect.BlurRadius);
                Assert.Equal(0.8, determinateEffect.Opacity);
                Assert.Equal(0, determinateEffect.ShadowDepth);
                Assert.Equal(determinateEffect.Color, indeterminateEffect.Color);
                Assert.Equal(determinateEffect.BlurRadius, indeterminateEffect.BlurRadius);
                Assert.Equal(determinateEffect.Opacity, indeterminateEffect.Opacity);
                Assert.Null(track.Effect);
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);

        thread.Start();
        thread.Join();

        Assert.Null(failure);
    }

    private static IEnumerable<T> FindVisualDescendants<T>(DependencyObject parent)
        where T : DependencyObject
    {
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match)
            {
                yield return match;
            }

            foreach (T descendant in FindVisualDescendants<T>(child))
            {
                yield return descendant;
            }
        }
    }
}
