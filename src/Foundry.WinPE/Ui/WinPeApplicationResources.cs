// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Windows;

namespace Foundry.WinPE.Ui;

/// <summary>
/// The application resources of the Foundry.WinPE windows, merged in order: Fluent first, where ThemeService swaps it, then
/// the shared Foundry.Wpf styles, then, in the UI process, the Connect and Deploy styles, which build on both.
/// </summary>
/// <remarks>
/// A StaticResource in a dictionary loaded through <see cref="ResourceDictionary.Source"/> is resolved while that dictionary
/// loads, against the resources already attached to the application. <see cref="Apply"/> therefore attaches the application
/// dictionary first and adds each merged dictionary after the ones it builds on, as a compiled <c>App.xaml</c> does. A
/// dictionary built completely before it is attached leaves the Connect and Deploy styles without their base styles.
/// </remarks>
internal static class WinPeApplicationResources
{
    private const string Fluent = "pack://application:,,,/PresentationFramework.Fluent;component/Themes/Fluent.xaml";
    private const string FoundryWpfStyles = "pack://application:,,,/Foundry.Wpf;component/Themes/FoundryWpfStyles.xaml";
    private const string ConnectStyles = "pack://application:,,,/Foundry.Connect;component/Themes/ConnectStyles.xaml";
    private const string DeployStyles = "pack://application:,,,/Foundry.Deploy;component/Themes/DeployStyles.xaml";

    /// <summary>The resources of the standalone windows: the obsolete-media window and the crash window.</summary>
    internal static IReadOnlyList<string> Standalone { get; } = [Fluent, FoundryWpfStyles];

    /// <summary>The resources of the UI process, which runs the Connect and Deploy phases.</summary>
    internal static IReadOnlyList<string> UiProcess { get; } = [Fluent, FoundryWpfStyles, ConnectStyles, DeployStyles];

    /// <summary>Replaces the application resources with <paramref name="dictionaries"/>, merged in order.</summary>
    internal static void Apply(Application application, IReadOnlyList<string> dictionaries)
    {
        var resources = new ResourceDictionary();
        application.Resources = resources;
        foreach (string source in dictionaries)
        {
            resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri(source, UriKind.Absolute) });
        }
    }
}
