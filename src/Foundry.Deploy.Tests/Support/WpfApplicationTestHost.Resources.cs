// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Windows;

namespace Foundry.Deploy.Tests;

internal static partial class WpfApplicationTestHost
{
    // The Deploy phase's resources, in the order of the Foundry.WinPE application resources.
    private static partial void AddResources(Application application)
    {
        var resources = new ResourceDictionary();
        application.Resources = resources;
        foreach (string source in new[]
        {
            "pack://application:,,,/PresentationFramework.Fluent;component/Themes/Fluent.xaml",
            "pack://application:,,,/Foundry.Wpf;component/Themes/FoundryWpfStyles.xaml",
            "pack://application:,,,/Foundry.Deploy;component/Themes/DeployStyles.xaml"
        })
        {
            resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri(source, UriKind.Absolute) });
        }
    }
}
