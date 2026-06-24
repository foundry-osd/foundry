// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Services.Updates;

/// <summary>
/// Represents the outcome of downloading an available application update.
/// </summary>
/// <param name="Status">Lifecycle status after the download attempt.</param>
/// <param name="Message">User-visible completion or failure message.</param>
public sealed record ApplicationUpdateDownloadResult(
    ApplicationUpdateStatus Status,
    string Message);
