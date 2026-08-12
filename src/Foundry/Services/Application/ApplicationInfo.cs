// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Diagnostics;

namespace Foundry.Services.Application;

internal static class ApplicationInfo
{
    private static readonly string ExecutablePathValue = Environment.ProcessPath
        ?? throw new InvalidOperationException("The Foundry executable path is unavailable.");
    private static readonly FileVersionInfo VersionInfo = FileVersionInfo.GetVersionInfo(ExecutablePathValue);

    public static string ProductName => VersionInfo.ProductName ?? "Foundry OSD";

    public static string Version => VersionInfo.FileVersion ?? "0.0.0.0";

    public static string VersionWithPrefix => $"v{Version}";

    public static string ProductNameAndVersion => $"{ProductName} {VersionWithPrefix}";

    public static string ExecutablePath => ExecutablePathValue;
}
