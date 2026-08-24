// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Foundry.Deploy.Motion;

internal static class TransitionAnimator
{
    private static readonly TimeSpan Duration = TimeSpan.FromMilliseconds(220);

    public static void FadeAndTranslateX(UIElement element, TranslateTransform transform, double offset)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(transform);

        element.BeginAnimation(UIElement.OpacityProperty, null);
        transform.BeginAnimation(TranslateTransform.XProperty, null);
        element.BeginAnimation(
            UIElement.OpacityProperty,
            CreateAnimation(0, 1),
            HandoffBehavior.SnapshotAndReplace);
        transform.BeginAnimation(
            TranslateTransform.XProperty,
            CreateAnimation(offset, 0),
            HandoffBehavior.SnapshotAndReplace);
    }

    public static void FadeAndTranslateY(UIElement element, TranslateTransform transform, double offset)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(transform);

        element.BeginAnimation(UIElement.OpacityProperty, null);
        transform.BeginAnimation(TranslateTransform.YProperty, null);
        element.BeginAnimation(
            UIElement.OpacityProperty,
            CreateAnimation(0, 1),
            HandoffBehavior.SnapshotAndReplace);
        transform.BeginAnimation(
            TranslateTransform.YProperty,
            CreateAnimation(offset, 0),
            HandoffBehavior.SnapshotAndReplace);
    }

    public static void FadeAndScale(
        UIElement element,
        ScaleTransform transform,
        double initialScale,
        double initialOpacity = 0)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(transform);

        element.BeginAnimation(UIElement.OpacityProperty, null);
        transform.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        transform.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        element.BeginAnimation(
            UIElement.OpacityProperty,
            CreateAnimation(initialOpacity, 1),
            HandoffBehavior.SnapshotAndReplace);
        transform.BeginAnimation(
            ScaleTransform.ScaleXProperty,
            CreateAnimation(initialScale, 1),
            HandoffBehavior.SnapshotAndReplace);
        transform.BeginAnimation(
            ScaleTransform.ScaleYProperty,
            CreateAnimation(initialScale, 1),
            HandoffBehavior.SnapshotAndReplace);
    }

    public static void Clear(UIElement element, TranslateTransform transform)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(transform);

        element.BeginAnimation(UIElement.OpacityProperty, null);
        transform.BeginAnimation(TranslateTransform.XProperty, null);
        transform.BeginAnimation(TranslateTransform.YProperty, null);
    }

    public static void Clear(UIElement element, ScaleTransform transform)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(transform);

        element.BeginAnimation(UIElement.OpacityProperty, null);
        transform.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        transform.BeginAnimation(ScaleTransform.ScaleYProperty, null);
    }

    private static DoubleAnimation CreateAnimation(double from, double to)
    {
        return new DoubleAnimation(from, to, Duration)
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
    }
}
