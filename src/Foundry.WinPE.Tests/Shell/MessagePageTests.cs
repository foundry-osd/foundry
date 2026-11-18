// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Windows;
using System.Windows.Controls;
using Foundry.Deploy.Tests;
using Foundry.WinPE.Shell;
using Foundry.WinPE.Tests.Support;
using Xunit;

namespace Foundry.WinPE.Tests.Shell;

/// <summary>Tests of the message page shown by the crash and obsolete-media screens.</summary>
[Collection(nameof(WpfApplicationHostCollection))]
public sealed class MessagePageTests
{
    [Fact]
    public Task Page_ShowsTheModelAndAPrimaryClickChoosesPrimary() => WpfApplicationTestHost.RunAsync(() =>
    {
        var model = new MessagePageModel("Title", "Message", "Relaunch", "Close");
        var page = new MessagePage(model);
        page.Measure(new Size(800, 600));

        string[] buttons = [.. FindButtons(page).Select(button => (string)button.Content)];
        // The accent (primary) button comes last, like in the message dialog.
        Assert.Equal(["Close", "Relaunch"], buttons);
        Assert.Equal(MessageChoice.None, model.Choice);

        FindButtons(page).Last().RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));

        Assert.Equal(MessageChoice.Primary, model.Choice);
        return Task.CompletedTask;
    });

    [Fact]
    public Task Page_WithoutSecondaryText_ShowsOneButtonAndTheSecondaryClickChoosesSecondary() => WpfApplicationTestHost.RunAsync(() =>
    {
        var single = new MessagePage(new MessagePageModel("Title", "Message", "Close"));
        single.Measure(new Size(800, 600));
        Assert.Single(FindButtons(single));

        var model = new MessagePageModel("Title", "Message", "Relaunch", "Close");
        var page = new MessagePage(model);
        page.Measure(new Size(800, 600));
        FindButtons(page).First().RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));

        Assert.Equal(MessageChoice.Secondary, model.Choice);
        return Task.CompletedTask;
    });

    private static IEnumerable<Button> FindButtons(DependencyObject root)
    {
        for (int index = 0; index < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); index++)
        {
            DependencyObject child = System.Windows.Media.VisualTreeHelper.GetChild(root, index);
            if (child is Button { Visibility: Visibility.Visible } button) yield return button;
            foreach (Button nested in FindButtons(child)) yield return nested;
        }
    }
}
