// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;

namespace Foundry.ViewModels;

/// <summary>
/// Displays library file metadata and inspection results without retaining answer-file content.
/// </summary>
public sealed record UnattendFileEntryViewModel(
    UnattendFileSettings Settings,
    string ArchitectureText,
    string ValidationMessage,
    bool IsValid)
{
    public string DisplayName => Settings.DisplayName;

    public string FileName => Settings.File?.FileName ?? string.Empty;
}

/// <summary>
/// Represents a custom default or the explicit native Foundry choice when its ID is null.
/// </summary>
public sealed record UnattendDefaultOption(string? Id, string DisplayName);
