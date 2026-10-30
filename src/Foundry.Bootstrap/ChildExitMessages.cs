// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Runtime;

namespace Foundry.Bootstrap;

/// <summary>Maps a child application exit code to the console message shown when the child stopped the boot.</summary>
internal static class ChildExitMessages
{
    /// <summary>
    /// Returns a specific message for the <see cref="ChildRuntimeExitCode"/> failure codes returned by Foundry Connect
    /// and a generic message for any other code or stage.
    /// </summary>
    internal static string Describe(BootstrapStage stage, int exitCode)
    {
        if (stage == BootstrapStage.Connect)
        {
            switch ((ChildRuntimeExitCode)exitCode)
            {
                case ChildRuntimeExitCode.StartupFailure:
                    return "Foundry Connect could not start. Check the session log for details.";
                case ChildRuntimeExitCode.ConfigurationFailure:
                    return "Foundry Connect could not load its configuration. Recreate the boot media.";
                case ChildRuntimeExitCode.RuntimeFailure:
                    return "Foundry Connect stopped unexpectedly. Check the session log for details.";
            }
        }

        return $"Foundry {(stage == BootstrapStage.Connect ? "Connect" : "Deploy")} stopped with exit code {exitCode}.";
    }
}
