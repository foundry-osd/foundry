// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.ComponentModel;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Foundry.Deploy.Tests;
using Foundry.WinPE.Shell;
using Foundry.WinPE.Tests.Support;
using Xunit;

namespace Foundry.WinPE.Tests.Shell;

/// <summary>Tests of the shell window that hosts the Connect and Deploy pages in the single WinPE UI process.</summary>
[Collection(nameof(WpfApplicationHostCollection))]
public sealed partial class WinPeShellWindowTests
{
    [Fact]
    public Task ShowContent_ReplacesTheContentAndFocusesIt() => WpfApplicationTestHost.RunAsync(async () =>
    {
        var window = new WinPeShellWindow();
        try
        {
            window.Show();
            Button first = new();
            Button second = new();
            window.ShowContent(first);
            await Settle();
            Assert.Same(first, GetPresenter(window).Content);

            window.ShowContent(second);
            await Settle();

            Assert.Same(second, GetPresenter(window).Content);
            Assert.Same(second, System.Windows.Input.FocusManager.GetFocusedElement(window));
        }
        finally
        {
            CloseForTest(window);
        }
    });

    [Fact]
    public Task CloseRequested_WithoutHandler_RefusesTheClose() => WpfApplicationTestHost.RunAsync(() =>
    {
        var window = new WinPeShellWindow();
        try
        {
            window.Show();
            window.Close();

            Assert.True(window.IsVisible);
        }
        finally
        {
            CloseForTest(window);
        }

        return Task.CompletedTask;
    });

    [Fact]
    public Task CloseRequested_HandlerCancel_KeepsTheWindowOpen() => WpfApplicationTestHost.RunAsync(() =>
    {
        var window = new WinPeShellWindow();
        try
        {
            int raised = 0;
            window.CloseRequested += (_, e) =>
            {
                raised++;
                e.Cancel = true;
            };
            window.Show();
            window.Close();

            Assert.Equal(1, raised);
            Assert.True(window.IsVisible);
        }
        finally
        {
            CloseForTest(window);
        }

        return Task.CompletedTask;
    });

    [Fact]
    public Task ShowContent_KeepsTheFocusThePageChoseItself() => WpfApplicationTestHost.RunAsync(async () =>
    {
        var window = new WinPeShellWindow();
        try
        {
            window.Show();
            Button chosen = new();
            StackPanel page = new() { Children = { new Button(), chosen } };
            page.Loaded += (_, _) => chosen.Focus();
            window.ShowContent(page);
            await Settle();

            Assert.Same(chosen, System.Windows.Input.FocusManager.GetFocusedElement(window));
        }
        finally
        {
            CloseForTest(window);
        }
    });

    [Fact]
    public Task AllowClose_ClosesTheWindowDespiteACancellingHandler() => WpfApplicationTestHost.RunAsync(() =>
    {
        var window = new WinPeShellWindow();
        bool closed = false;
        window.Closed += (_, _) => closed = true;
        window.CloseRequested += (_, e) => e.Cancel = true;
        window.Show();
        window.AllowClose = true;
        window.Close();

        Assert.True(closed);
        return Task.CompletedTask;
    });

    [Fact]
    public Task FirstContentRendered_IsRaisedOnce() => WpfApplicationTestHost.RunAsync(async () =>
    {
        var window = new WinPeShellWindow();
        try
        {
            int raised = 0;
            window.FirstContentRendered += (_, _) => raised++;
            window.Show();
            window.ShowContent(new Button());
            await Settle();
            window.ShowContent(new Button());
            await Settle();

            Assert.Equal(1, raised);
        }
        finally
        {
            CloseForTest(window);
        }
    });

    [Fact]
    public Task Window_RendersWithTheStandaloneResourcesOnly() => WpfApplicationTestHost.RunAsync(async () =>
    {
        var window = new WinPeShellWindow();
        try
        {
            window.Show();
            window.ShowContent(new TextBlock { Text = "page" });
            await Settle();

            Assert.True(window.IsVisible);
            Assert.Empty(WpfApplicationTestHost.UnhandledExceptions);
        }
        finally
        {
            CloseForTest(window);
        }
    });

    // The host merges every UI-process resource, so absence cannot be proven at runtime: the guardian hosts the window with
    // the standalone resources only, which the source must respect.
    [Fact]
    public void WindowSource_ReferencesNoConnectOrDeployOnlyResource()
    {
        string root = RepositoryRoot.Find();
        string xaml = File.ReadAllText(Path.Combine(root, "src", "Foundry.WinPE", "Shell", "WinPeShellWindow.xaml"));
        HashSet<string> applicationKeys = [.. ReadKeys(Path.Combine(root, "src", "Foundry.Connect", "Themes", "ConnectStyles.xaml")),
            .. ReadKeys(Path.Combine(root, "src", "Foundry.Deploy", "Themes", "DeployStyles.xaml"))];
        applicationKeys.ExceptWith(ReadKeys(Path.Combine(root, "src", "Foundry.Wpf", "Themes", "FoundryWpfStyles.xaml")));

        string[] referenced = [.. ResourceReference().Matches(xaml).Select(match => match.Groups[1].Value)];

        foreach (string key in referenced)
        {
            Assert.DoesNotContain(key, applicationKeys);
        }
    }

    private static IEnumerable<string> ReadKeys(string path) =>
        XKey().Matches(File.ReadAllText(path)).Select(match => match.Groups[1].Value);

    private static ContentPresenter GetPresenter(WinPeShellWindow window) =>
        (ContentPresenter)window.FindName("ContentHost");

    private static void CloseForTest(WinPeShellWindow window)
    {
        window.AllowClose = true;
        window.Close();
    }

    private static Task Settle() => Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle).Task;

    [GeneratedRegex("x:Key=\"([^\"]+)\"")]
    private static partial Regex XKey();

    [GeneratedRegex(@"\{(?:StaticResource|DynamicResource)\s+(?:ResourceKey=)?([^\s}]+)\s*\}")]
    private static partial Regex ResourceReference();
}
