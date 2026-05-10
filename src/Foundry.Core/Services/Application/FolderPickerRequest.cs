namespace Foundry.Core.Services.Application;

public sealed record FolderPickerRequest(
    string Title,
    string? SuggestedFolderPath = null);
