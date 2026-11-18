// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Runtime;

namespace Foundry.WinPE;

/// <summary>Maps a UI process exit code to the console message shown when the UI process stopped the boot.</summary>
internal static class ChildExitMessages
{
    /// <summary>
    /// Returns a specific message for the <see cref="ChildRuntimeExitCode"/> failure codes, which the UI process returns only
    /// when its Foundry Connect phase failed, and a generic message describing the UI process as "Foundry" for any other code.
    /// </summary>
    internal static string Describe(int exitCode) => (ChildRuntimeExitCode)exitCode switch
    {
        ChildRuntimeExitCode.StartupFailure => "Foundry Connect could not start. Check the session log for details.",
        ChildRuntimeExitCode.ConfigurationFailure => "Foundry Connect could not load its configuration. Recreate the boot media.",
        ChildRuntimeExitCode.RuntimeFailure => "Foundry Connect stopped unexpectedly. Check the session log for details.",
        _ => $"Foundry stopped unexpectedly with exit code {exitCode}. Check the session log for details."
    };
}
