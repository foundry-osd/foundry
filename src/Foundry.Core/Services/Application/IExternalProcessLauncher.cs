namespace Foundry.Core.Services.Application;

public interface IExternalProcessLauncher
{
    Task OpenUriAsync(Uri uri);
    Task OpenFolderAsync(string folderPath);
    Task OpenFileAsync(string filePath);
}
