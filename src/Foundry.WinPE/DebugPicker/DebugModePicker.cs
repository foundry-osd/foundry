// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Windows;
using System.Windows.Controls;

namespace Foundry.WinPE.DebugPicker;

/// <summary>What the developer chose in the debug mode picker.</summary>
internal enum DebugModeChoice
{
    /// <summary>The picker was closed without a choice.</summary>
    None,

    /// <summary>The <c>--ui</c> session: Foundry Connect, then Foundry Deploy.</summary>
    ConnectThenDeploy,

    /// <summary>The UI session without the Connect phase: no key, default theme and language, no system preparation.</summary>
    DeployOnly,

    /// <summary>The obsolete-media screen, as under <c>Foundry.Deploy.exe</c>, without readiness reporting.</summary>
    ObsoleteMedia
}

/// <summary>
/// Lets a developer pick a mode when Foundry.WinPE starts without an argument in <c>DebugSafetyMode</c> (Debug build with a
/// debugger attached), instead of running the guardian, which requires Windows PE.
/// </summary>
/// <remarks>
/// Unreachable in Release builds and with any argument, so the production launch contract (<c>--ui</c>,
/// <c>--native-deployment-worker</c>) is unchanged; "Deploy only" exists only here, never as an argument. The window is shown
/// before any <see cref="Application"/> exists, because the chosen mode creates its own.
/// </remarks>
internal static class DebugModePicker
{
    /// <summary>Returns whether the picker replaces the guardian: debug safety on and no argument.</summary>
    internal static bool ShouldShow(bool debugSafetyEnabled, IReadOnlyList<string> args) => debugSafetyEnabled && args.Count == 0;

    /// <summary>Runs the chosen mode and returns its exit code; no choice exits with 0.</summary>
    /// <param name="choice">The developer's choice.</param>
    /// <param name="runUi">Runs the UI process; its argument skips the Connect phase.</param>
    /// <param name="runObsoleteMedia">Shows the obsolete-media screen.</param>
    internal static int Run(DebugModeChoice choice, Func<bool, int> runUi, Func<int> runObsoleteMedia) => choice switch
    {
        DebugModeChoice.ConnectThenDeploy => runUi(false),
        DebugModeChoice.DeployOnly => runUi(true),
        DebugModeChoice.ObsoleteMedia => runObsoleteMedia(),
        _ => 0
    };

    /// <summary>Shows the picker modally on the calling STA thread and returns the choice.</summary>
    internal static DebugModeChoice Show()
    {
        DebugModeChoice choice = DebugModeChoice.None;
        var panel = new StackPanel { Margin = new Thickness(16) };
        var window = new Window
        {
            Title = "Foundry.WinPE debug mode",
            Content = panel,
            SizeToContent = SizeToContent.WidthAndHeight,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterScreen
        };
        panel.Children.Add(new TextBlock
        {
            Text = "Debug build with a debugger attached. Choose what to run:",
            Margin = new Thickness(0, 0, 0, 8)
        });
        AddChoice("Connect then Deploy", DebugModeChoice.ConnectThenDeploy);
        AddChoice("Deploy only", DebugModeChoice.DeployOnly);
        AddChoice("Obsolete-media screen", DebugModeChoice.ObsoleteMedia);
        window.ShowDialog();
        return choice;

        void AddChoice(string text, DebugModeChoice value)
        {
            var button = new Button { Content = text, MinWidth = 280, Margin = new Thickness(0, 4, 0, 0), Padding = new Thickness(12, 6, 12, 6) };
            button.Click += (_, _) =>
            {
                choice = value;
                window.Close();
            };
            panel.Children.Add(button);
        }
    }
}
