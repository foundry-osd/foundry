// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Deploy.Services.Deployment.Native;

namespace Foundry.WinPE;

/// <summary>Selects what one Foundry.WinPE process runs.</summary>
internal enum WinPeMode
{
    /// <summary>The boot guardian started by the WinPE launcher.</summary>
    Guardian,

    /// <summary>The UI process: Foundry Connect, then system preparation, then Foundry Deploy, launched by the guardian.</summary>
    Ui,

    /// <summary>The native deployment worker, launched by Foundry Deploy.</summary>
    NativeWorker
}

/// <summary>Parses the command line of a Foundry.WinPE process into its <see cref="WinPeMode"/>.</summary>
/// <remarks>
/// The guardian launches its own executable with a leading <c>--ui</c>; Foundry Deploy launches it with
/// <c>--native-deployment-worker</c>. These are the internal launch contract, not operator options. The worker flag wins
/// wherever it appears, so a worker process never starts a UI or a second guardian. Any other command line, including none,
/// runs the guardian. Matching is ordinal.
/// </remarks>
internal static class WinPeModes
{
    internal const string UiArgument = "--ui";
    internal const string NativeWorkerArgument = NativeDeploymentWorker.Command;

    internal static WinPeMode Parse(IReadOnlyList<string> args)
    {
        if (args.Contains(NativeWorkerArgument, StringComparer.Ordinal)) return WinPeMode.NativeWorker;
        return args.Count > 0 && args[0] == UiArgument ? WinPeMode.Ui : WinPeMode.Guardian;
    }
}
