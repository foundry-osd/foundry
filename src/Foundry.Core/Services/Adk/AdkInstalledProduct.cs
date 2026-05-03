namespace Foundry.Core.Services.Adk;

public sealed record AdkInstalledProduct(
    string DisplayName,
    string? DisplayVersion,
    string? UninstallString = null,
    string? QuietUninstallString = null);
