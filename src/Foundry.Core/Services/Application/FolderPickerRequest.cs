// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Services.Application;

public sealed record FolderPickerRequest(
    string Title,
    string? SuggestedFolderPath = null);
