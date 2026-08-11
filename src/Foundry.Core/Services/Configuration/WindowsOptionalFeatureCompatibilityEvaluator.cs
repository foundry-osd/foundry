// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.WinPe;

namespace Foundry.Core.Services.Configuration;

/// <summary>
/// Evaluates documented optional-feature compatibility for authoring guidance.
/// </summary>
public static class WindowsOptionalFeatureCompatibilityEvaluator
{
    public static WindowsOptionalFeatureCompatibility Evaluate(
        string featureId,
        IEnumerable<string> editionNames,
        IEnumerable<string> releaseIds,
        WinPeArchitecture architecture)
    {
        int?[] builds = releaseIds
            .Select(OperatingSystemSelectionCatalog.FindRelease)
            .Select(release => release?.Build)
            .ToArray();

        return EvaluateResolved(featureId, editionNames, builds, architecture);
    }

    public static WindowsOptionalFeatureCompatibility EvaluateBuilds(
        string featureId,
        IEnumerable<string> editionNames,
        IEnumerable<int> builds,
        WinPeArchitecture architecture)
    {
        return EvaluateResolved(featureId, editionNames, builds.Select(build => (int?)build), architecture);
    }

    private static WindowsOptionalFeatureCompatibility EvaluateResolved(
        string featureId,
        IEnumerable<string> editionNames,
        IEnumerable<int?> builds,
        WinPeArchitecture architecture)
    {
        WindowsOptionalFeatureCatalogEntry? entry = WindowsOptionalFeatureCatalog.GetEffectiveEntry(featureId);
        if (entry is null)
        {
            return WindowsOptionalFeatureCompatibility.Unavailable;
        }

        bool hasArchitectureRestriction = entry.SupportedArchitectures.Count > 0 &&
                                          entry.SupportedArchitectures.Count < Enum.GetValues<WinPeArchitecture>().Length;
        bool hasEditionRestriction = entry.KnownSupportedEditionIds.Count > 0 || entry.KnownUnsupportedEditionIds.Count > 0;
        bool hasBuildRestriction = entry.MinimumBuild.HasValue || entry.MaximumBuildExclusive.HasValue;
        if (!hasArchitectureRestriction && !hasEditionRestriction && !hasBuildRestriction)
        {
            return WindowsOptionalFeatureCompatibility.RuntimeVerificationRequired;
        }

        if (entry.SupportedArchitectures.Count > 0 && !entry.SupportedArchitectures.Contains(architecture))
        {
            return WindowsOptionalFeatureCompatibility.Unavailable;
        }

        WindowsEditionDefinition?[] editions = editionNames.Select(WindowsEditionCatalog.Find).ToArray();
        int?[] targetBuilds = builds.ToArray();
        if (editions.Length == 0 || targetBuilds.Length == 0)
        {
            return WindowsOptionalFeatureCompatibility.RuntimeVerificationRequired;
        }

        List<bool?> results = [];
        foreach (WindowsEditionDefinition? edition in editions)
        {
            foreach (int? build in targetBuilds)
            {
                bool? editionResult = EvaluateEdition(entry, edition);
                bool? buildResult = EvaluateBuild(entry, build);
                results.Add(editionResult == false || buildResult == false
                    ? false
                    : editionResult is null || buildResult is null
                        ? null
                        : true);
            }
        }

        if (results.Any(result => result is null))
        {
            return WindowsOptionalFeatureCompatibility.RuntimeVerificationRequired;
        }

        bool anyAvailable = results.Any(result => result == true);
        bool anyUnavailable = results.Any(result => result == false);
        return (anyAvailable, anyUnavailable) switch
        {
            (true, true) => WindowsOptionalFeatureCompatibility.PartiallyAvailable,
            (true, false) => WindowsOptionalFeatureCompatibility.Available,
            _ => WindowsOptionalFeatureCompatibility.Unavailable
        };
    }

    private static bool? EvaluateEdition(WindowsOptionalFeatureCatalogEntry entry, WindowsEditionDefinition? edition)
    {
        if (edition is null)
        {
            return null;
        }

        if (entry.KnownUnsupportedEditionIds.Contains(edition.EditionId, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        return entry.KnownSupportedEditionIds.Count == 0
            ? true
            : entry.KnownSupportedEditionIds.Contains(edition.EditionId, StringComparer.OrdinalIgnoreCase);
    }

    private static bool? EvaluateBuild(WindowsOptionalFeatureCatalogEntry entry, int? build)
    {
        if (!entry.MinimumBuild.HasValue && !entry.MaximumBuildExclusive.HasValue)
        {
            return true;
        }

        if (!build.HasValue)
        {
            return null;
        }

        return (!entry.MinimumBuild.HasValue || build.Value >= entry.MinimumBuild.Value) &&
               (!entry.MaximumBuildExclusive.HasValue || build.Value < entry.MaximumBuildExclusive.Value);
    }
}
