// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Windows;

namespace Foundry.Deploy.Tests;

internal static partial class WpfApplicationTestHost
{
    // The Deploy phase's resources, in the order of the Foundry.WinPE application resources.
    private static partial ResourceDictionary CreateResources() => new()
    {
        MergedDictionaries =
        {
            new ResourceDictionary { Source = new Uri("pack://application:,,,/PresentationFramework.Fluent;component/Themes/Fluent.xaml", UriKind.Absolute) },
            new ResourceDictionary { Source = new Uri("pack://application:,,,/Foundry.Wpf;component/Themes/FoundryWpfStyles.xaml", UriKind.Absolute) },
            new ResourceDictionary { Source = new Uri("pack://application:,,,/Foundry.Deploy;component/Themes/DeployApplicationResources.xaml", UriKind.Absolute) }
        }
    };
}
