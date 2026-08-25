// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Globalization;
using Foundry.Deploy.Models.Configuration;
using Foundry.Deploy.ViewModels;

namespace Foundry.Deploy.Tests;

public sealed class WindowsCustomizationSummaryBuilderTests
{
    [Fact]
    public void Build_WhenNothingIsConfigured_ReturnsOnlyNoChangesStatus()
    {
        IReadOnlyList<DeploymentSummaryRowViewModel> rows = WindowsCustomizationSummaryBuilder.Build(
            new DeployOobeSettings(),
            new DeployAppxRemovalSettings(),
            new DeployAiComponentRemovalSettings(),
            new DeployWindowsOptionalFeatureSettings(),
            key => key,
            CultureInfo.InvariantCulture);

        DeploymentSummaryRowViewModel row = Assert.Single(rows);
        Assert.Equal(DeploymentSummaryRowKind.Value, row.Kind);
        Assert.Equal("Summary.Status", row.Label);
        Assert.Equal("Summary.Status.NoChanges", row.Value);
    }

    [Fact]
    public void Build_WhenOnlyOptionalFeaturesAreConfigured_OmitsInactiveSectionsAndSeparators()
    {
        var optionalFeatures = new DeployWindowsOptionalFeatureSettings
        {
            IsEnabled = true,
            Actions =
            [
                new DeployWindowsOptionalFeatureAction { Id = "TelnetClient", Enable = true },
                new DeployWindowsOptionalFeatureAction { Id = "WorkFolders-Client", Enable = false }
            ]
        };

        IReadOnlyList<DeploymentSummaryRowViewModel> rows = WindowsCustomizationSummaryBuilder.Build(
            new DeployOobeSettings(),
            new DeployAppxRemovalSettings(),
            new DeployAiComponentRemovalSettings(),
            optionalFeatures,
            key => key,
            CultureInfo.InvariantCulture);

        Assert.Equal(
            [
                DeploymentSummaryRowKind.Section,
                DeploymentSummaryRowKind.Value,
                DeploymentSummaryRowKind.Value,
                DeploymentSummaryRowKind.Value
            ],
            rows.Select(row => row.Kind));
        Assert.Equal("Summary.WindowsOptionalFeatures", rows[0].Label);
        Assert.DoesNotContain(rows, row => row.Kind == DeploymentSummaryRowKind.Separator);
        Assert.DoesNotContain(rows, row => row.Label == "Summary.Oobe");
        Assert.DoesNotContain(rows, row => row.Label == "Summary.ApplicationAndAiRemoval");
    }
}
