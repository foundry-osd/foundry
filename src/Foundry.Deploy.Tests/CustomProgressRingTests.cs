// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Windows;
using System.Windows.Threading;
using Foundry.Deploy.Controls;

namespace Foundry.Deploy.Tests;

public sealed class CustomProgressRingTests
{
    [Fact]
    public void DeterminateAnimation_CompletesWithoutMutatingFrozenTimeline()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            Window? window = null;
            try
            {
                var ring = new CustomProgressRing
                {
                    Width = 180,
                    Height = 180
                };
                window = new Window
                {
                    Width = 200,
                    Height = 200,
                    Left = -10000,
                    ShowInTaskbar = false,
                    WindowStyle = WindowStyle.None,
                    Content = ring
                };
                window.Show();
                ring.Value = 20;
                if (!ring.HasAnimatedProperties)
                {
                    throw new InvalidOperationException("The determinate animation did not start.");
                }

                var frame = new DispatcherFrame();
                var timer = new DispatcherTimer(DispatcherPriority.Background)
                {
                    Interval = TimeSpan.FromSeconds(1)
                };
                timer.Tick += (_, _) =>
                {
                    timer.Stop();
                    frame.Continue = false;
                };
                timer.Start();
                Dispatcher.PushFrame(frame);
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            finally
            {
                window?.Close();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);

        thread.Start();
        thread.Join();

        Assert.Null(failure);
    }
}
