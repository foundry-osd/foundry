// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Windows;

// The namespace of the linked host from Foundry.Deploy.Tests, which this part completes.
namespace Foundry.Deploy.Tests;

internal static partial class WpfApplicationTestHost
{
    // The Connect phase's resources, in the order of the Foundry.WinPE application resources.
    private static partial void AddResources(Application application)
    {
        var resources = new ResourceDictionary();
        application.Resources = resources;
        foreach (string source in new[]
        {
            "pack://application:,,,/PresentationFramework.Fluent;component/Themes/Fluent.xaml",
            "pack://application:,,,/Foundry.Wpf;component/Themes/FoundryWpfStyles.xaml",
            "pack://application:,,,/Foundry.Connect;component/Themes/ConnectStyles.xaml"
        })
        {
            resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri(source, UriKind.Absolute) });
        }
    }
}
