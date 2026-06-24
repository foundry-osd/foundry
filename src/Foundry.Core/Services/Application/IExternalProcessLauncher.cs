// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Services.Application;

public interface IExternalProcessLauncher
{
    Task OpenUriAsync(Uri uri);
    Task OpenFolderAsync(string folderPath);
    Task OpenFileAsync(string filePath);
}
