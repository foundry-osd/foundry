// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Services.Configuration;

/// <summary>
/// Reports structural applicability and conservative compatibility signals without exposing XML values.
/// </summary>
public sealed record UnattendInspection
{
    public const string FirstBootLauncherDescription = "Foundry first-boot provisioning";
    public const string FirstBootLauncherCommand = @"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe -NoProfile -ExecutionPolicy Bypass -File %SystemRoot%\Temp\Foundry\PreOobe\Invoke-FoundryPreOobe.ps1";

    /// <summary>Gets whether exactly one applicable specialize command invokes the supported Foundry runner.</summary>
    public bool HasFoundrySpecializeLauncher { get; init; }
    /// <summary>
    /// Gets declared architectures in supported components.
    /// </summary>
    public IReadOnlyList<string> Architectures { get; init; } = [];

    /// <summary>
    /// Gets whether applicable components contain command settings requiring compatibility review.
    /// </summary>
    public bool HasCommands { get; init; }

    /// <summary>
    /// Gets whether applicable known settings take ownership of enrollment-sensitive OOBE or accounts.
    /// </summary>
    public bool ConflictsWithAutopilot { get; init; }
}
