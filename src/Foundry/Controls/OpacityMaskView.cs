// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Microsoft.UI.Composition;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using System.Numerics;

namespace Foundry.Controls;

/// <summary>
/// Applies the alpha channel of a XAML element as an opacity mask over its content.
/// </summary>
[TemplatePart(Name = RootGridTemplateName, Type = typeof(Grid))]
[TemplatePart(Name = MaskContainerTemplateName, Type = typeof(Border))]
[TemplatePart(Name = ContentPresenterTemplateName, Type = typeof(ContentPresenter))]
public sealed partial class OpacityMaskView : ContentControl
{
    public static readonly DependencyProperty OpacityMaskProperty = DependencyProperty.Register(
        nameof(OpacityMask),
        typeof(UIElement),
        typeof(OpacityMaskView),
        new PropertyMetadata(null, OnOpacityMaskChanged));

    private const string ContentPresenterTemplateName = "PART_ContentPresenter";
    private const string MaskContainerTemplateName = "PART_MaskContainer";
    private const string RootGridTemplateName = "PART_RootGrid";

    private readonly Compositor compositor = CompositionTarget.GetCompositorForCurrentThread();
    private CompositionBrush? mask;
    private CompositionMaskBrush? maskBrush;

    /// <summary>
    /// Initializes a new instance of the <see cref="OpacityMaskView"/> class.
    /// </summary>
    public OpacityMaskView()
    {
        DefaultStyleKey = typeof(OpacityMaskView);
    }

    /// <summary>
    /// Gets or sets the element whose alpha channel masks the rendered content.
    /// </summary>
    public UIElement? OpacityMask
    {
        get => (UIElement?)GetValue(OpacityMaskProperty);
        set => SetValue(OpacityMaskProperty, value);
    }

    /// <inheritdoc />
    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        if (GetTemplateChild(RootGridTemplateName) is not Grid rootGrid
            || GetTemplateChild(ContentPresenterTemplateName) is not ContentPresenter contentPresenter
            || GetTemplateChild(MaskContainerTemplateName) is not Border maskContainer)
        {
            return;
        }

        maskBrush = compositor.CreateMaskBrush();
        maskBrush.Source = CreateVisualBrush(contentPresenter);
        mask = CreateVisualBrush(maskContainer);
        maskBrush.Mask = OpacityMask is null ? null : mask;

        SpriteVisual redirectVisual = compositor.CreateSpriteVisual();
        redirectVisual.RelativeSizeAdjustment = Vector2.One;
        redirectVisual.Brush = maskBrush;
        ElementCompositionPreview.SetElementChildVisual(rootGrid, redirectVisual);
    }

    private static CompositionBrush CreateVisualBrush(UIElement element)
    {
        Visual visual = ElementCompositionPreview.GetElementVisual(element);
        Compositor compositor = visual.Compositor;
        CompositionVisualSurface visualSurface = compositor.CreateVisualSurface();
        visualSurface.SourceVisual = visual;

        ExpressionAnimation sourceSizeAnimation = compositor.CreateExpressionAnimation($"{nameof(visual)}.Size");
        sourceSizeAnimation.SetReferenceParameter(nameof(visual), visual);
        visualSurface.StartAnimation(nameof(visualSurface.SourceSize), sourceSizeAnimation);

        CompositionSurfaceBrush brush = compositor.CreateSurfaceBrush(visualSurface);
        visual.Opacity = 0;
        return brush;
    }

    private static void OnOpacityMaskChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        OpacityMaskView view = (OpacityMaskView)dependencyObject;
        if (view.maskBrush is null)
        {
            return;
        }

        view.maskBrush.Mask = e.NewValue is null ? null : view.mask;
    }
}
