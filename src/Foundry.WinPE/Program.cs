// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.WinPE;

/// <summary>Entry point of Foundry.WinPE.exe: dispatches one process to the guardian, a UI or the native worker.</summary>
/// <remarks>
/// This entry point owns the STA thread that the Foundry Connect and Foundry Deploy WPF applications run on. The executable
/// uses the console subsystem, so the guardian writes to the startnet console and every mode inherits it without opening
/// another window.
/// </remarks>
public static class Program
{
    /// <summary>Runs the mode selected by <see cref="WinPeModes.Parse"/> and returns its process exit code.</summary>
    [STAThread]
    public static int Main(string[] args) => WinPeModes.Parse(args) switch
    {
        // Deploy's worker check expects the worker flag first; it already is when Deploy launches the worker.
        WinPeMode.NativeWorker => Foundry.Deploy.Program.Run(args[Array.IndexOf(args, WinPeModes.NativeWorkerArgument)..]),
        WinPeMode.Connect => Foundry.Connect.Program.Run(WinPeModes.StripMode(args)),
        WinPeMode.Deploy => Foundry.Deploy.Program.Run(WinPeModes.StripMode(args)),
        _ => GuardianProgram.RunAsync(args).GetAwaiter().GetResult()
    };
}
