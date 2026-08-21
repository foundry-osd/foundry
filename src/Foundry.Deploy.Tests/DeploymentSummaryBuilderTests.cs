// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Deploy.Services.Wizard;
using Foundry.Deploy.ViewModels;

namespace Foundry.Deploy.Tests;

public sealed class DeploymentSummaryBuilderTests
{
    [Fact]
    public void Build_AlwaysReturnsSevenCategoriesInApprovedOrder()
    {
        var builder = new DeploymentSummaryBuilder(key => key);

        IReadOnlyList<DeploymentSummaryCategoryViewModel> categories = builder.Build(CreateSource());

        Assert.Equal(
            [
                "Summary.Category.TargetDevice",
                "Summary.Category.OperatingSystem",
                "Summary.Category.Drivers",
                "Summary.Category.Autopilot",
                "Summary.Category.WindowsCustomization",
                "Summary.Category.Network",
                "Summary.Category.Completion"
            ],
            categories.Select(category => category.Title));
    }

    [Fact]
    public void Build_ShowsAbsentAutopilotAsNeutralWithoutEditAction()
    {
        var builder = new DeploymentSummaryBuilder(key => key);

        DeploymentSummaryCategoryViewModel autopilot = builder.Build(CreateSource())[3];

        Assert.Equal(DeploymentSummaryStatus.Neutral, autopilot.Status);
        Assert.Equal("Summary.Status.NotConfigured", autopilot.Summary);
        Assert.Null(autopilot.EditStepId);
    }

    [Fact]
    public void Build_UsesCautionOnlyForNonBlockingTargetWarning()
    {
        var builder = new DeploymentSummaryBuilder(key => key);
        DeploymentSummarySource source = CreateSource() with { HasTargetWarning = true };

        IReadOnlyList<DeploymentSummaryCategoryViewModel> categories = builder.Build(source);

        Assert.Equal(DeploymentSummaryStatus.Caution, categories[0].Status);
        Assert.All(categories.Skip(1), category => Assert.NotEqual(DeploymentSummaryStatus.Caution, category.Status));
    }

    private static DeploymentSummarySource CreateSource()
    {
        return new DeploymentSummarySource
        {
            TargetSummary = "PC-001",
            TargetRows = [new("Computer name", "PC-001")],
            OperatingSystemSummary = "Windows 11",
            OperatingSystemRows = [new("Edition", "Enterprise")],
            DriversSummary = "None",
            DriverRows = [],
            IsDriversConfigured = false,
            IsAutopilotConfigured = false,
            HasAutopilotStep = false,
            AutopilotSummary = string.Empty,
            AutopilotRows = [],
            IsWindowsCustomizationConfigured = false,
            WindowsCustomizationSummary = "No changes",
            WindowsCustomizationRows = [],
            IsNetworkConfigured = false,
            NetworkSummary = "Not configured",
            NetworkRows = [],
            CompletionSummary = "Automatic restart",
            CompletionRows = []
        };
    }
}
