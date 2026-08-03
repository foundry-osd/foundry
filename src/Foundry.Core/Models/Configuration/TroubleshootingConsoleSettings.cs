// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Models.Configuration;

/// <summary>
/// Describes the shortcut that opens an interactive PowerShell troubleshooting console from the WinPE
/// runtime apps (Foundry.Connect and Foundry.Deploy).
/// </summary>
/// <remarks>
/// The console is owned by the runtime apps rather than the boot image so it is only reachable while
/// Foundry is on screen. It is disabled by default to discourage tampering with production media.
/// </remarks>
public sealed record TroubleshootingConsoleSettings
{
    /// <summary>
    /// Gets a value indicating whether the troubleshooting console shortcut is active.
    /// </summary>
    public bool IsEnabled { get; init; }

    /// <summary>
    /// Gets the key that opens the console.
    /// </summary>
    public TroubleshootingConsoleKey Key { get; init; } = TroubleshootingConsoleKey.F8;

    /// <summary>
    /// Gets the modifier that must be held with <see cref="Key"/>.
    /// </summary>
    public TroubleshootingConsoleModifier Modifier { get; init; } = TroubleshootingConsoleModifier.None;
}
