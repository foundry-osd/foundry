// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Text;

namespace Foundry.Utilities.Processes;

/// <summary>
/// Creates command-line arguments for encoded PowerShell scripts.
/// </summary>
public static class PowerShellCommand
{
    /// <summary>
    /// Encodes a script as UTF-16LE Base64 and returns independent PowerShell argument tokens.
    /// </summary>
    /// <param name="script">The PowerShell script to execute.</param>
    public static IReadOnlyList<string> CreateEncodedArguments(string script)
    {
        ArgumentNullException.ThrowIfNull(script);

        string encodedScript = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        return
        [
            "-EncodedCommand",
            encodedScript
        ];
    }
}
