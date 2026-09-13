// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Services.Application;

/// <summary>Describes an open-file selection; a zero-based initial filter overrides the picker default (-1).</summary>
public sealed record FileOpenPickerRequest(
    string Title,
    IReadOnlyList<string> FileTypeFilters,
    string? SuggestedFolderPath = null,
    int InitialFileTypeIndex = -1);
