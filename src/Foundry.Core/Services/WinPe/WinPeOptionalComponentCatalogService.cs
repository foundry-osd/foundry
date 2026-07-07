// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Services.WinPe;

/// <inheritdoc />
public sealed class WinPeOptionalComponentCatalogService : IWinPeOptionalComponentCatalogService
{
    /// <inheritdoc />
    public WinPeResult<IReadOnlyList<WinPeOptionalComponent>> GetAvailableComponents(
        string kitsRootPath,
        WinPeArchitecture architecture)
    {
        if (string.IsNullOrWhiteSpace(kitsRootPath))
        {
            return WinPeResult<IReadOnlyList<WinPeOptionalComponent>>.Failure(
                WinPeErrorCodes.ValidationFailed,
                "ADK kits root path is required.",
                "Provide a resolved ADK KitsRoot10 path.");
        }

        if (!Enum.IsDefined(architecture))
        {
            return WinPeResult<IReadOnlyList<WinPeOptionalComponent>>.Failure(
                WinPeErrorCodes.ValidationFailed,
                "WinPE architecture value is invalid.",
                $"Value: '{architecture}'.");
        }

        string optionalComponentsRoot = WinPeOptionalComponentPaths.GetOptionalComponentsRootPath(kitsRootPath, architecture);
        if (!Directory.Exists(optionalComponentsRoot))
        {
            return WinPeResult<IReadOnlyList<WinPeOptionalComponent>>.Failure(
                WinPeErrorCodes.ToolNotFound,
                "The WinPE optional components folder was not found.",
                $"Expected path: '{optionalComponentsRoot}'. Install the Windows PE add-on for the ADK.");
        }

        List<WinPeOptionalComponent> components = [];
        foreach (string cabPath in Directory.EnumerateFiles(optionalComponentsRoot, "*.cab", SearchOption.TopDirectoryOnly))
        {
            string name = Path.GetFileNameWithoutExtension(cabPath);

            // Only the neutral WinPE-* component cabs are user-selectable; the language pack (lp.cab) and
            // any localized cabs live in per-language subfolders and are added automatically.
            if (!name.StartsWith("WinPE-", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            components.Add(new WinPeOptionalComponent
            {
                Name = name,
                NeutralCabPath = cabPath,
                IsRecommendedDefault = WinPeOptionalComponentDefaults.IsRecommendedDefault(name)
            });
        }

        IReadOnlyList<WinPeOptionalComponent> sorted = components
            .OrderBy(component => component.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return WinPeResult<IReadOnlyList<WinPeOptionalComponent>>.Success(sorted);
    }
}
