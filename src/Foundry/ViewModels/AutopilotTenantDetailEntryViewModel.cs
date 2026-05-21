namespace Foundry.ViewModels;

/// <summary>
/// Represents one tenant metadata row displayed after a successful tenant connection.
/// </summary>
public sealed record AutopilotTenantDetailEntryViewModel(string Name, string Value);
