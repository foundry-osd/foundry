// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Diagnostics.CodeAnalysis;
using Foundry.Core.Models.Runtime;
using Foundry.Core.Services.Runtime;

namespace Foundry.WinPE.ObsoleteMedia;

/// <summary>
/// Detects and runs the obsolete-media mode: the legacy <c>Foundry.Connect-&lt;rid&gt;.zip</c> and
/// <c>Foundry.Deploy-&lt;rid&gt;.zip</c> release assets ship Foundry.WinPE renamed <c>Foundry.Connect.exe</c> and
/// <c>Foundry.Deploy.exe</c>, which the launcher of boot media created by Foundry OSD v26.10.18.1 or earlier downloads
/// and starts. Under those names the executable only tells the operator to recreate the media.
/// </summary>
/// <remarks>
/// The legacy launchers wait for the startup protocol handshake and report a startup error without <c>ui_ready</c>, so
/// this mode acknowledges <c>managed_started</c> and <c>ui_ready</c> like the applications it replaces. It never reports
/// <c>startup_failed</c>, never runs the guardian or a deployment, and always ends as an operator abort.
/// </remarks>
internal static class ObsoleteMediaMode
{
    private const string ConnectApplicationId = "Foundry.Connect";
    private const string DeployApplicationId = "Foundry.Deploy";
    private static readonly string[] ApplicationIds = [ConnectApplicationId, DeployApplicationId];

    /// <summary>Exit code returned once the window is closed: the legacy launchers treat it as an operator abort.</summary>
    internal const int ExitCode = (int)ChildRuntimeExitCode.UserAborted;

    /// <summary>
    /// Returns the legacy application identity when the process runs under a legacy executable name (case-insensitive)
    /// and is not the native deployment worker, which Foundry Deploy starts from its own executable path.
    /// </summary>
    internal static bool TryGetApplicationId(
        string? processPath,
        IReadOnlyList<string> args,
        [NotNullWhen(true)] out string? applicationId)
    {
        applicationId = null;
        if (processPath is null || WinPeModes.Parse(args) == WinPeMode.NativeWorker) return false;
        string fileName = Path.GetFileName(processPath);
        foreach (string candidate in ApplicationIds)
        {
            if (string.Equals(fileName, candidate + ".exe", StringComparison.OrdinalIgnoreCase))
            {
                applicationId = candidate;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Reports <c>managed_started</c>, shows the window until it is closed and returns <see cref="ExitCode"/>. The
    /// window calls the supplied callback once its content is rendered, which reports <c>ui_ready</c>. Reporting is a
    /// no-op without the legacy startup protocol variables.
    /// </summary>
    /// <param name="applicationId"><c>Foundry.Connect</c> or <c>Foundry.Deploy</c>.</param>
    /// <param name="showWindow">Shows the blocking window and invokes its argument after the first render.</param>
    internal static int Run(string applicationId, Action<Action> showWindow)
    {
        RuntimeStartupReporter? reporter = RuntimeStartupReporter.FromEnvironment(applicationId);
        reporter?.Report(StartupStage.ManagedStarted);
        showWindow(() => reporter?.Report(StartupStage.UiReady));
        return ExitCode;
    }
}
