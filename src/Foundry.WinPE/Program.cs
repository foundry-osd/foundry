// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Deploy.Services.Deployment.Native;
using Foundry.WinPE.DebugPicker;
using Foundry.WinPE.ObsoleteMedia;
using Foundry.WinPE.Ui;
using Foundry.Wpf.Runtime;
using MessageBox = System.Windows.MessageBox;

namespace Foundry.WinPE;

/// <summary>Entry point of Foundry.WinPE.exe: dispatches one process to the guardian, the UI process or the native worker.</summary>
/// <remarks>
/// This entry point owns the STA thread that the UI process runs its WPF application on. The executable uses the console
/// subsystem, so the guardian writes to the startnet console and the other modes inherit it without opening another window.
/// Renamed <c>Foundry.Connect.exe</c> or <c>Foundry.Deploy.exe</c> by the legacy release assets, it only shows the
/// obsolete-media window (see <see cref="ObsoleteMediaMode"/>) before any mode is parsed. In <see cref="DebugSafetyMode"/>,
/// without an argument, the <see cref="DebugModePicker"/> replaces the guardian.
/// </remarks>
public static class Program
{
    /// <summary>Runs the mode selected by <see cref="WinPeModes.Parse"/> and returns its process exit code.</summary>
    [STAThread]
    public static int Main(string[] args)
    {
        if (ObsoleteMediaMode.TryGetApplicationId(Environment.ProcessPath, args, out string? applicationId))
        {
            return ObsoleteMediaMode.Run(applicationId, ObsoleteMediaWindow.Show);
        }

        if (DebugModePicker.ShouldShow(DebugSafetyMode.IsEnabled, args))
        {
            // WPF caches its AppContext switches when it first reads them, which the picker's window can already do.
            WinPeUiHost.ConfigureFluentBackdrop();
            return DebugModePicker.Run(DebugModePicker.Show(), WinPeUiHost.Run, ShowObsoleteMediaScreen, ShowCrashScreen);
        }

        return WinPeModes.Parse(args) switch
        {
            // Deploy starts the worker with the worker flag first, as NativeDeploymentWorker.Run expects.
            WinPeMode.NativeWorker => NativeDeploymentWorker.Run(args[Array.IndexOf(args, WinPeModes.NativeWorkerArgument)..]),
            WinPeMode.Ui => WinPeUiHost.Run(skipConnect: false),
            _ => GuardianProgram.RunAsync(args).GetAwaiter().GetResult()
        };
    }

    // The debug picker previews the screen without the legacy startup protocol, so nothing is reported.
    private static int ShowObsoleteMediaScreen()
    {
        ObsoleteMediaWindow.Show(static () => { });
        return ObsoleteMediaMode.ExitCode;
    }

    // The debug picker previews the crash window on this thread and only reports the choice: nothing is relaunched.
    private static void ShowCrashScreen(bool relaunchAllowed)
    {
        UiCrashChoice choice = UiCrashWindow.Show(relaunchAllowed, Path.Combine(Path.GetTempPath(), "Foundry", "Logs"));
        MessageBox.Show($"Crash screen choice: {choice}.", "Foundry.WinPE debug mode");
    }
}
