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
                var verticalStepper = Assert.IsType<ItemsControl>(wizardView.FindName("VerticalStepper"));
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
                wizardView.Measure(new Size(1440, 900));
                wizardView.Arrange(new Rect(0, 0, 1440, 900));
                wizardView.UpdateLayout();
                Assert.Equal(wizardContentCard.ActualWidth, wizardFooter.ActualWidth);
                Assert.Equal(980, wizardContentCard.ActualWidth, precision: 3);
                double stepperLeft = verticalStepper.TranslatePoint(new Point(), wizardView).X;
                double stepperRight = verticalStepper.TranslatePoint(
                    new Point(verticalStepper.ActualWidth, 0),
                    wizardView).X;
                Point cardOrigin = wizardContentCard.TranslatePoint(new Point(), wizardView);
                double cardRight = cardOrigin.X + wizardContentCard.ActualWidth;
                Assert.Equal(16, cardOrigin.X - stepperRight, precision: 3);
                Assert.Equal(stepperLeft, 1440 - cardRight, precision: 3);
                Assert.Equal(8, verticalStepper.TranslatePoint(new Point(), wizardContentCard).Y, precision: 3);

                var narrowWizardView = new WizardView();
                var narrowVerticalStepper = Assert.IsType<ItemsControl>(
                    narrowWizardView.FindName("VerticalStepper"));
                narrowWizardView.Measure(new Size(800, 700));
                narrowWizardView.Arrange(new Rect(0, 0, 800, 700));
                narrowWizardView.UpdateLayout();
                Assert.Equal(Visibility.Visible, narrowVerticalStepper.Visibility);

                var splashView = new SplashView();
                var landingContent = Assert.IsType<Grid>(splashView.FindName("LandingContent"));
                splashView.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
                Assert.False(
                    landingContent.HasAnimatedProperties,
                    "The Welcome page should be stable when the application opens.");

                var targetStepView = new TargetStepView();
                var targetHeader = Assert.IsType<WizardPageHeader>(targetStepView.FindName("PageHeader"));
                var deploymentSettings = Assert.IsType<TextBlock>(targetStepView.FindName("DeploymentSettingsTitle"));
                var deviceInventory = Assert.IsType<TextBlock>(targetStepView.FindName("DeviceInventoryTitle"));
                var diskEraseNotice = Assert.IsType<Border>(targetStepView.FindName("DiskEraseNotice"));
                var deviceInventorySeparator = Assert.IsType<Separator>(targetStepView.FindName("DeviceInventorySeparator"));
                var deviceInventoryGrid = Assert.IsType<Grid>(targetStepView.FindName("DeviceInventoryGrid"));
                var firmwareSeparator = Assert.IsType<Separator>(targetStepView.FindName("FirmwareSeparator"));
                Assert.Equal("\uE772", targetHeader.Glyph);
                Assert.Equal(new Thickness(0, 0, 0, 32), targetHeader.Margin);
                Assert.Same(application.FindResource("SubtitleTextBlockStyle"), deploymentSettings.Style);
                Assert.Same(application.FindResource("SubtitleTextBlockStyle"), deviceInventory.Style);
                Assert.Same(application.FindResource("SystemFillColorCautionBrush"), diskEraseNotice.BorderBrush);
                Assert.Equal(new Thickness(0, 0, 0, 12), diskEraseNotice.Margin);
                Assert.Equal(new Thickness(0, 0, 0, 12), deviceInventorySeparator.Margin);
                Assert.Equal(new Thickness(0, 12, 0, 12), firmwareSeparator.Margin);
                Assert.Equal(5, deviceInventoryGrid.ColumnDefinitions.Count);
                Assert.Equal(new GridLength(2, GridUnitType.Star), deviceInventoryGrid.ColumnDefinitions[0].Width);

                var operatingSystemHeader = Assert.IsType<WizardPageHeader>(
                    new OperatingSystemCatalogStepView().FindName("PageHeader"));
                var driversHeader = Assert.IsType<WizardPageHeader>(
                    new DriverPackStepView().FindName("PageHeader"));
                var autopilotHeader = Assert.IsType<WizardPageHeader>(
                    new AutopilotStepView().FindName("PageHeader"));
                Assert.Equal("\uEC77", operatingSystemHeader.Glyph);
                Assert.Equal("\uE74C", driversHeader.Glyph);
                Assert.Equal("\uE753", autopilotHeader.Glyph);

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
                Assert.Equal(16, verticalConnector.ActualHeight);
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

                var deploymentStatusView = new DeploymentStatusView();
                var stepsRail = Assert.IsType<Grid>(deploymentStatusView.FindName("StepsRail"));
                var centralStateHost = Assert.IsType<Grid>(deploymentStatusView.FindName("CentralStateHost"));
                var successGlyph = Assert.IsType<TerminalStatusGlyph>(deploymentStatusView.FindName("SuccessGlyph"));
                var successTitle = Assert.IsType<TextBlock>(deploymentStatusView.FindName("SuccessTitle"));
                var successInstruction = Assert.IsType<TextBlock>(deploymentStatusView.FindName("SuccessInstruction"));
                var successButton = Assert.IsType<Button>(deploymentStatusView.FindName("SuccessButton"));
                var errorGlyph = Assert.IsType<TerminalStatusGlyph>(deploymentStatusView.FindName("ErrorGlyph"));
                var errorTitle = Assert.IsType<TextBlock>(deploymentStatusView.FindName("ErrorTitle"));
                var failedStep = Assert.IsType<TextBlock>(deploymentStatusView.FindName("FailedStep"));
                var technicalDetailsButton = Assert.IsType<Button>(deploymentStatusView.FindName("TechnicalDetailsButton"));
                Assert.Equal(VerticalAlignment.Center, stepsRail.VerticalAlignment);
                Assert.Equal(720, stepsRail.MaxHeight);
                Assert.IsType<ScaleTransform>(centralStateHost.RenderTransform);
                Assert.Equal(new Point(0.5, 0.5), centralStateHost.RenderTransformOrigin);
                Assert.Same(application.FindResource("TitleTextBlockStyle"), successTitle.Style);
                Assert.Same(application.FindResource("TitleTextBlockStyle"), errorTitle.Style);

                successTitle.Text = "Deployment complete";
                successInstruction.Text = "Remove the boot media, then select Reboot.";
                successTitle.Visibility = Visibility.Visible;
                successGlyph.Visibility = Visibility.Visible;
                successInstruction.Visibility = Visibility.Visible;
                successButton.Visibility = Visibility.Visible;
                errorTitle.Visibility = Visibility.Collapsed;
                errorGlyph.Visibility = Visibility.Collapsed;
                failedStep.Visibility = Visibility.Collapsed;
                technicalDetailsButton.Visibility = Visibility.Collapsed;
                deploymentStatusView.Measure(new Size(1440, 900));
                deploymentStatusView.Arrange(new Rect(0, 0, 1440, 900));
                deploymentStatusView.UpdateLayout();
                Assert.Equal(24, VerticalGap(successTitle, successGlyph, deploymentStatusView), precision: 3);
                Assert.Equal(24, VerticalGap(successGlyph, successInstruction, deploymentStatusView), precision: 3);
                Assert.Equal(16, VerticalGap(successInstruction, successButton, deploymentStatusView), precision: 3);

                successTitle.Visibility = Visibility.Collapsed;
                successGlyph.Visibility = Visibility.Collapsed;
                successInstruction.Visibility = Visibility.Collapsed;
                successButton.Visibility = Visibility.Collapsed;
                errorTitle.Text = "Deployment failed";
                failedStep.Text = "Apply operating system image";
                errorTitle.Visibility = Visibility.Visible;
                errorGlyph.Visibility = Visibility.Visible;
                failedStep.Visibility = Visibility.Visible;
                technicalDetailsButton.Visibility = Visibility.Visible;
                deploymentStatusView.UpdateLayout();
                Assert.Equal(24, VerticalGap(errorTitle, errorGlyph, deploymentStatusView), precision: 3);
                Assert.Equal(24, VerticalGap(errorGlyph, failedStep, deploymentStatusView), precision: 3);
                Assert.Equal(16, VerticalGap(failedStep, technicalDetailsButton, deploymentStatusView), precision: 3);

                var summaryStepView = new SummaryStepView();
                var summaryRoot = Assert.IsType<StackPanel>(summaryStepView.Content);
                var summaryPageHeader = Assert.IsType<WizardPageHeader>(summaryStepView.FindName("PageHeader"));
                Assert.Equal("\uE9D5", summaryPageHeader.Glyph);
                Assert.Null(summaryStepView.FindName("DiskEraseNotice"));
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

    private static double VerticalGap(
        FrameworkElement upperElement,
        FrameworkElement lowerElement,
        UIElement relativeTo)
    {
        double upperBottom = upperElement.TranslatePoint(
            new Point(0, upperElement.ActualHeight),
            relativeTo).Y;
        double lowerTop = lowerElement.TranslatePoint(new Point(), relativeTo).Y;
        return lowerTop - upperBottom;
    }
}
