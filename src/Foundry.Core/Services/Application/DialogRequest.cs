// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Services.Application;

/// <summary>
/// Describes a platform-neutral informational dialog request.
/// </summary>
/// <param name="Title">Dialog title shown to the user.</param>
/// <param name="Message">Dialog body message.</param>
/// <param name="CloseButtonText">Localized text for the button that dismisses the dialog.</param>
public sealed record DialogRequest(
    string Title,
    string Message,
    string CloseButtonText);
