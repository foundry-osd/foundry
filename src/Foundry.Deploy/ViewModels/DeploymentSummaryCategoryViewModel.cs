// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Deploy.Services.Wizard;

namespace Foundry.Deploy.ViewModels;

public enum DeploymentSummaryStatus
{
    Configured,
    Neutral,
    Caution
}

public sealed record DeploymentSummaryRowViewModel(string Label, string Value);

public sealed record DeploymentSummaryCategoryViewModel(
    string Title,
    string Summary,
    DeploymentSummaryStatus Status,
    IReadOnlyList<DeploymentSummaryRowViewModel> Rows,
    DeploymentWizardStepId? EditStepId)
{
    public string Glyph => Status switch
    {
        DeploymentSummaryStatus.Configured => "\uE930",
        DeploymentSummaryStatus.Caution => "\uE7BA",
        _ => "\uECCA"
    };

    public string BrushResourceKey => Status switch
    {
        DeploymentSummaryStatus.Configured => "SystemFillColorSuccessBrush",
        DeploymentSummaryStatus.Caution => "SystemFillColorCautionBrush",
        _ => "TextFillColorSecondaryBrush"
    };

    public bool CanEdit => EditStepId.HasValue;
}

public sealed record DeploymentSummarySource
{
    public required string TargetSummary { get; init; }
    public required IReadOnlyList<DeploymentSummaryRowViewModel> TargetRows { get; init; }
    public bool HasTargetWarning { get; init; }
    public required string OperatingSystemSummary { get; init; }
    public required IReadOnlyList<DeploymentSummaryRowViewModel> OperatingSystemRows { get; init; }
    public required string DriversSummary { get; init; }
    public required IReadOnlyList<DeploymentSummaryRowViewModel> DriverRows { get; init; }
    public bool IsDriversConfigured { get; init; }
    public required string AutopilotSummary { get; init; }
    public required IReadOnlyList<DeploymentSummaryRowViewModel> AutopilotRows { get; init; }
    public bool IsAutopilotConfigured { get; init; }
    public bool HasAutopilotStep { get; init; }
    public required string WindowsCustomizationSummary { get; init; }
    public required IReadOnlyList<DeploymentSummaryRowViewModel> WindowsCustomizationRows { get; init; }
    public bool IsWindowsCustomizationConfigured { get; init; }
    public required string NetworkSummary { get; init; }
    public required IReadOnlyList<DeploymentSummaryRowViewModel> NetworkRows { get; init; }
    public bool IsNetworkConfigured { get; init; }
    public required string CompletionSummary { get; init; }
    public required IReadOnlyList<DeploymentSummaryRowViewModel> CompletionRows { get; init; }
}

public sealed class DeploymentSummaryBuilder
{
    private readonly Func<string, string> _localize;

    public DeploymentSummaryBuilder(Func<string, string> localize)
    {
        _localize = localize ?? throw new ArgumentNullException(nameof(localize));
    }

    public IReadOnlyList<DeploymentSummaryCategoryViewModel> Build(DeploymentSummarySource source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return
        [
            new(
                _localize("Summary.Category.TargetDevice"),
                source.TargetSummary,
                source.HasTargetWarning ? DeploymentSummaryStatus.Caution : DeploymentSummaryStatus.Configured,
                source.TargetRows,
                DeploymentWizardStepId.TargetDevice),
            new(
                _localize("Summary.Category.OperatingSystem"),
                source.OperatingSystemSummary,
                DeploymentSummaryStatus.Configured,
                source.OperatingSystemRows,
                DeploymentWizardStepId.OperatingSystem),
            new(
                _localize("Summary.Category.Drivers"),
                source.DriversSummary,
                source.IsDriversConfigured ? DeploymentSummaryStatus.Configured : DeploymentSummaryStatus.Neutral,
                source.DriverRows,
                DeploymentWizardStepId.Drivers),
            new(
                _localize("Summary.Category.Autopilot"),
                source.IsAutopilotConfigured ? source.AutopilotSummary : _localize("Summary.Status.NotConfigured"),
                source.IsAutopilotConfigured ? DeploymentSummaryStatus.Configured : DeploymentSummaryStatus.Neutral,
                source.AutopilotRows,
                source.HasAutopilotStep ? DeploymentWizardStepId.Autopilot : null),
            new(
                _localize("Summary.Category.WindowsCustomization"),
                source.WindowsCustomizationSummary,
                source.IsWindowsCustomizationConfigured ? DeploymentSummaryStatus.Configured : DeploymentSummaryStatus.Neutral,
                source.WindowsCustomizationRows,
                null),
            new(
                _localize("Summary.Category.Network"),
                source.NetworkSummary,
                source.IsNetworkConfigured ? DeploymentSummaryStatus.Configured : DeploymentSummaryStatus.Neutral,
                source.NetworkRows,
                null),
            new(
                _localize("Summary.Category.Completion"),
                source.CompletionSummary,
                DeploymentSummaryStatus.Configured,
                source.CompletionRows,
                null)
        ];
    }
}
