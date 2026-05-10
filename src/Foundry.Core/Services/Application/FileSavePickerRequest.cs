namespace Foundry.Core.Services.Application;

public sealed record FileSavePickerRequest(
    string Title,
    string SuggestedFileName,
    IReadOnlyList<FilePickerTypeChoice> FileTypeChoices,
    string? DefaultFileExtension = null,
    string? SuggestedFolderPath = null);
