// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.WinPE;

/// <summary>Selects what one Foundry.WinPE process runs.</summary>
internal enum WinPeMode
{
    /// <summary>The boot guardian started by the WinPE launcher.</summary>
    Guardian,

    /// <summary>The Foundry Connect UI, launched by the guardian.</summary>
    Connect,

    /// <summary>The Foundry Deploy UI, launched by the guardian.</summary>
    Deploy,

    /// <summary>The native deployment worker, launched by Foundry Deploy.</summary>
    NativeWorker
}

/// <summary>Parses the command line of a Foundry.WinPE process into its <see cref="WinPeMode"/>.</summary>
/// <remarks>
/// The guardian launches its own executable with a leading <c>--connect</c> or <c>--deploy</c>; Foundry Deploy launches it
/// with <c>--native-deployment-worker</c>. The worker flag wins wherever it appears, so a worker process never starts a UI
/// or a second guardian. Any other command line, including none, runs the guardian. Matching is ordinal.
/// </remarks>
internal static class WinPeModes
{
    internal const string ConnectArgument = "--connect";
    internal const string DeployArgument = "--deploy";
    // Must match Foundry.Deploy's NativeDeploymentWorker.Command, which is internal to that assembly.
    internal const string NativeWorkerArgument = "--native-deployment-worker";

    internal static WinPeMode Parse(IReadOnlyList<string> args)
    {
        if (args.Contains(NativeWorkerArgument, StringComparer.Ordinal)) return WinPeMode.NativeWorker;
        return args.Count == 0 ? WinPeMode.Guardian : args[0] switch
        {
            ConnectArgument => WinPeMode.Connect,
            DeployArgument => WinPeMode.Deploy,
            _ => WinPeMode.Guardian
        };
    }

    /// <summary>Returns the arguments after the leading mode argument, for the application that mode runs.</summary>
    internal static string[] StripMode(string[] args) => args is [ConnectArgument or DeployArgument, ..] ? args[1..] : args;
}
