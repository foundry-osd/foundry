namespace Foundry.Core.Services.Application;

public interface IFilePickerService
{
    Task<string?> PickOpenFileAsync(FileOpenPickerRequest request);
    Task<string?> PickSaveFileAsync(FileSavePickerRequest request);
    Task<string?> PickFolderAsync(FolderPickerRequest request);
}
