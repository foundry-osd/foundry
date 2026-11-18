// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.WinPE.Ui;
using System.Windows;
using Foundry.Connect.Views;
using Foundry.Deploy.Tests;
using Xunit;

namespace Foundry.WinPE.Tests.Ui;

/// <summary>
/// Smoke tests of the UI process's resources (<see cref="WinPeApplicationResources.UiProcess"/>): every resource of every
/// merged dictionary loads, so the Connect and Deploy styles resolve the shared Foundry.Wpf and Fluent styles they are based
/// on, and the Connect page loads with them.
/// </summary>
[Collection(nameof(WpfApplicationHostCollection))]
public sealed class ApplicationResourcesTests
{
    [Fact]
    public Task EveryApplicationResource_Loads() => WpfApplicationTestHost.RunAsync(() =>
    {
        var failures = new List<string>();
        Collect(Application.Current.Resources, failures);

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
        return Task.CompletedTask;
    });

    [Fact]
    public Task ConnectPage_WithApplicationResources_Loads() => WpfApplicationTestHost.RunAsync(() =>
    {
        // A missing resource or a parse error throws from the constructor.
        var page = new ConnectPage();
        page.Measure(new Size(1280, 800));
        page.Arrange(new Rect(0, 0, 1280, 800));
        page.UpdateLayout();

        Assert.Empty(WpfApplicationTestHost.UnhandledExceptions);
        return Task.CompletedTask;
    });

    private static void Collect(ResourceDictionary dictionary, List<string> failures)
    {
        foreach (ResourceDictionary merged in dictionary.MergedDictionaries)
        {
            Collect(merged, failures);
        }

        foreach (object key in dictionary.Keys.Cast<object>().ToList())
        {
            try
            {
                // Reading the value realizes a deferred resource; a StaticResource it cannot resolve leaves it null.
                if (dictionary[key] is null)
                {
                    failures.Add($"{dictionary.Source}: {key} is null");
                }
            }
            catch (Exception exception)
            {
                failures.Add($"{dictionary.Source}: {key}: {exception.GetBaseException().Message}");
            }
        }
    }
}
