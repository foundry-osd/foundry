// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Foundry.Wpf.Motion;

/// <summary>
/// Shared entrance animations (220 ms, quadratic ease-out) that fade an element in while moving or scaling
/// it to its resting state. Each call first drops any running animation on the same properties, so repeated
/// calls restart cleanly from the requested start values instead of stacking.
/// </summary>
/// <remarks>Must be called on the UI thread. The element transforms must be owned by the element, not frozen.</remarks>
public static class TransitionAnimator
{
    private static readonly TimeSpan Duration = TimeSpan.FromMilliseconds(220);

    /// <summary>Fades in and slides horizontally from <paramref name="offset"/> to 0.</summary>
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

    /// <summary>Fades in and slides vertically from <paramref name="offset"/> to 0.</summary>
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

    /// <summary>Fades in from <paramref name="initialOpacity"/> and scales both axes from <paramref name="initialScale"/> to 1.</summary>
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

    /// <summary>Stops running animations and releases opacity and translation to their local values.</summary>
    public static void Clear(UIElement element, TranslateTransform transform)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(transform);

        element.BeginAnimation(UIElement.OpacityProperty, null);
        transform.BeginAnimation(TranslateTransform.XProperty, null);
        transform.BeginAnimation(TranslateTransform.YProperty, null);
    }

    /// <summary>Stops running animations and releases opacity and scale to their local values.</summary>
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
