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
                Assert.Equal(980, wizardContentCard.MaxWidth);
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
