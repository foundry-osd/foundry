namespace Foundry.Core.Services.Application;

/// <summary>
/// Abstracts file and folder picker interactions for UI-independent services and view models.
/// </summary>
public interface IFilePickerService
{
    /// <summary>
    /// Shows an open-file picker.
    /// </summary>
    /// <param name="request">The picker request.</param>
    /// <returns>The selected file path, or <see langword="null"/> when canceled.</returns>
    Task<string?> PickOpenFileAsync(FileOpenPickerRequest request);

    /// <summary>
    /// Shows a save-file picker.
    /// </summary>
    /// <param name="request">The picker request.</param>
    /// <returns>The selected file path, or <see langword="null"/> when canceled.</returns>
    Task<string?> PickSaveFileAsync(FileSavePickerRequest request);

    /// <summary>
    /// Shows a folder picker.
    /// </summary>
    /// <param name="request">The picker request.</param>
    /// <returns>The selected folder path, or <see langword="null"/> when canceled.</returns>
    Task<string?> PickFolderAsync(FolderPickerRequest request);
}
