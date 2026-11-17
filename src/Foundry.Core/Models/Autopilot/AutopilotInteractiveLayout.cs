// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Models.Autopilot;

/// <summary>
/// Names and relative locations shared by Foundry Deploy, Foundry WinPE, Foundry PostInstall and
/// <c>Foundry.AutopilotInteractive</c>.
/// </summary>
/// <remarks>
/// Every <c>*RelativePath</c> is relative to the retained Foundry root in the target operating system,
/// <c>%SystemRoot%\Temp\Foundry</c>, and uses forward slashes so it can be combined on any platform.
/// </remarks>
public static class AutopilotInteractiveLayout
{
    /// <summary>Application name used for release assets and runtime archives.</summary>
    public const string ApplicationName = "Foundry.AutopilotInteractive";

    /// <summary>Executable file name inside the staged runtime directory.</summary>
    public const string ExecutableFileName = "Foundry.AutopilotInteractive.exe";

    /// <summary>Directory holding the staged executable and its configuration.</summary>
    public const string RuntimeRelativePath = "Runtime/AutopilotInteractive";

    /// <summary>Directory holding durable application state such as the registration result.</summary>
    public const string StateRelativePath = "State/AutopilotInteractive";

    /// <summary>Directory holding the application log files.</summary>
    public const string LogsRelativePath = "Logs/AutopilotInteractive";

    /// <summary>Name of the registration result file inside <see cref="StateRelativePath"/>.</summary>
    public const string ResultFileName = "result.json";
}
