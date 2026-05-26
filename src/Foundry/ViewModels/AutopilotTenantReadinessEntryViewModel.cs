using Microsoft.UI.Xaml.Media;

namespace Foundry.ViewModels;

/// <summary>
/// Represents one tenant readiness value displayed in the Autopilot hardware hash upload configuration.
/// </summary>
public sealed record AutopilotTenantReadinessEntryViewModel(string Name, string Value, Brush? ValueForeground);
