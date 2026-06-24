// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Services.WinPe;

/// <summary>
/// Resolves the architecture-specific OA3Tool executable from the Windows ADK deployment tools.
/// </summary>
public static class WinPeOa3ToolResolver
{
    /// <summary>
    /// Resolves the OA3Tool executable for the WinPE media architecture.
    /// </summary>
    /// <param name="kitsRootPath">Windows ADK KitsRoot10 path.</param>
    /// <param name="architecture">Target WinPE architecture.</param>
    /// <returns>The resolved OA3Tool path or a WinPE diagnostic.</returns>
    public static WinPeResult<string> Resolve(string kitsRootPath, WinPeArchitecture architecture)
    {
        if (string.IsNullOrWhiteSpace(kitsRootPath))
        {
            return WinPeResult<string>.Failure(
                WinPeErrorCodes.ValidationFailed,
                "ADK kits root path is required to resolve OA3Tool.",
                "Set the ADK KitsRoot10 path before generating media.");
        }

        if (!Enum.IsDefined(architecture))
        {
            return WinPeResult<string>.Failure(
                WinPeErrorCodes.ValidationFailed,
                "WinPE architecture value is invalid.",
                $"Value: '{architecture}'.");
        }

        string adkArchitecture = architecture.ToCopypeArchitecture();
        string[] candidates =
        [
            Path.Combine(
                kitsRootPath,
                "Assessment and Deployment Kit",
                "Deployment Tools",
                adkArchitecture,
                "Licensing",
                "OA30",
                "oa3tool.exe"),
            Path.Combine(
                kitsRootPath,
                "Deployment Tools",
                adkArchitecture,
                "Licensing",
                "OA30",
                "oa3tool.exe")
        ];

        foreach (string candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return WinPeResult<string>.Success(candidate);
            }
        }

        return WinPeResult<string>.Failure(
            WinPeErrorCodes.ToolNotFound,
            "OA3Tool executable was not found for the selected WinPE architecture.",
            $"Expected OA3Tool under ADK Deployment Tools for '{adkArchitecture}'.");
    }
}
