// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Foundry.Deploy.Motion;

namespace Foundry.Deploy.Tests;

public sealed class TransitionAnimatorTests
{
    [Fact]
    public void FadeAndTranslateY_StartsBelowTheFinalPosition()
    {
        RunInSta(() =>
        {
            var element = new Border();
            var transform = new TranslateTransform();
            element.RenderTransform = transform;
            using WindowScope window = ShowInWindow(element);

            TransitionAnimator.FadeAndTranslateY(element, transform, 12);
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Render);

            Assert.True(element.HasAnimatedProperties);
            Assert.InRange(element.Opacity, 0, 0.99);
            Assert.InRange(transform.Y, 0.01, 12);
            Assert.Equal(0, transform.X, precision: 3);
        });
    }

    [Fact]
    public void FadeAndTranslateX_PreservesTheNavigationDirection()
    {
        RunInSta(() =>
        {
            var element = new Border();
            var transform = new TranslateTransform();
            element.RenderTransform = transform;
            using WindowScope window = ShowInWindow(element);

            TransitionAnimator.FadeAndTranslateX(element, transform, -14);
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Render);

            Assert.True(element.HasAnimatedProperties);
            Assert.InRange(element.Opacity, 0, 0.99);
            Assert.InRange(transform.X, -14, -0.01);
            Assert.Equal(0, transform.Y, precision: 3);
        });
    }

    [Fact]
    public void FadeAndScale_StartsTheCompleteCompositionAtTheRequestedScale()
    {
        RunInSta(() =>
        {
            var element = new Grid();
            var transform = new ScaleTransform();
            element.RenderTransform = transform;
            using WindowScope window = ShowInWindow(element);

            TransitionAnimator.FadeAndScale(element, transform, 0.98);
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Render);

            Assert.True(element.HasAnimatedProperties);
            Assert.InRange(element.Opacity, 0, 0.99);
            Assert.InRange(transform.ScaleX, 0.98, 0.999);
            Assert.InRange(transform.ScaleY, 0.98, 0.999);
        });
    }

    [Fact]
    public void Clear_RemovesActiveElementAndTransformAnimations()
    {
        RunInSta(() =>
        {
            var element = new Grid();
            var transform = new TranslateTransform();
            element.RenderTransform = transform;
            using WindowScope window = ShowInWindow(element);
            TransitionAnimator.FadeAndTranslateX(element, transform, 14);

            TransitionAnimator.Clear(element, transform);

            Assert.False(element.HasAnimatedProperties);
            Assert.False(transform.HasAnimatedProperties);
        });
    }

    private static WindowScope ShowInWindow(UIElement content)
    {
        var window = new Window
        {
            Width = 200,
            Height = 200,
            Left = -10000,
            ShowInTaskbar = false,
            WindowStyle = WindowStyle.None,
            Content = content
        };
        window.Show();
        return new WindowScope(window);
    }

    private static void RunInSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
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

    private sealed class WindowScope(Window window) : IDisposable
    {
        public void Dispose()
        {
            window.Close();
        }
    }
}
