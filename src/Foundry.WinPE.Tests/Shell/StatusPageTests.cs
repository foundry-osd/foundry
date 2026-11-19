// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Foundry.Deploy.Tests;
using Foundry.Wpf.Controls;
using Foundry.Wpf.Shell;
using Foundry.WinPE.Shell;
using Xunit;

namespace Foundry.WinPE.Tests.Shell;

/// <summary>Tests of the shared status page rendered in the WinPE shell window.</summary>
[Collection(nameof(WpfApplicationHostCollection))]
public sealed class StatusPageTests
{
    [Fact]
    public Task Page_RendersTheModelInTheShellWithALiveStatusAndAnIndeterminateRing() => WpfApplicationTestHost.RunAsync(async () =>
    {
        var window = new WinPeShellWindow();
        try
        {
            var model = new StatusPageModel("Foundry Connect", "Looking for the network…");
            var page = new StatusPageView { DataContext = model };

            window.ShowContent(page);
            await Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle).Task;

            string[] texts = [.. Find<TextBlock>(page).Select(block => block.Text)];
            Assert.Contains("Foundry Connect", texts);
            Assert.Contains("Looking for the network…", texts);
            TextBlock status = Find<TextBlock>(page).Single(block => block.Text == "Looking for the network…");
            Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(status));
            Assert.True(Find<CustomProgressRing>(page).Single().IsIndeterminate);

            model.Status = "Setting the clock…";
            Assert.Equal("Setting the clock…", status.Text);
        }
        finally
        {
            window.AllowClose = true;
            window.Close();
        }
    });

    private static IEnumerable<T> Find<T>(DependencyObject root)
        where T : DependencyObject
    {
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, index);
            if (child is T match) yield return match;
            foreach (T nested in Find<T>(child)) yield return nested;
        }
    }
}
