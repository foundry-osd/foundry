// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Models.Autopilot;

/// <summary>
/// Names and relative locations shared by Foundry Deploy, Foundry Bootstrap, Foundry PostInstall and
/// <c>Foundry.AutopilotInteractive</c>.
/// </summary>
/// <remarks>
/// Every <c>*RelativePath</c> is relative to the retained Foundry root in the target operating system,
/// <c>%SystemRoot%\Temp\Foundry</c>, and uses forward slashes so it can be combined on any platform.
/// </remarks>
public static class AutopilotInteractiveLayout
{
    /// <summary>Application name used for release assets, runtime archives and trust manifest entries.</summary>
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

    /// <summary>
    /// Environment variable through which Bootstrap forwards a debug archive override to Deploy. Must be set together with
    /// <see cref="ArchiveSha256EnvironmentVariable"/>.
    /// </summary>
    public const string ArchiveEnvironmentVariable = "FOUNDRY_AUTOPILOTINTERACTIVE_ARCHIVE";

    /// <summary>Environment variable carrying the SHA256 that authenticates the archive named by <see cref="ArchiveEnvironmentVariable"/>.</summary>
    public const string ArchiveSha256EnvironmentVariable = "FOUNDRY_AUTOPILOTINTERACTIVE_ARCHIVE_SHA256";
}
