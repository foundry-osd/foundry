namespace Foundry.Core.Services.Application;

public sealed record FileOpenPickerRequest(
    string Title,
    IReadOnlyList<string> FileTypeFilters,
    string? SuggestedFolderPath = null);
