// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Services.Operations;

/// <summary>
/// Identifies the terminal result displayed by the operation dialog.
/// </summary>
public enum OperationOutcome
{
    /// <summary>
    /// The operation has not finished.
    /// </summary>
    None,

    /// <summary>
    /// The operation completed successfully.
    /// </summary>
    Success,

    /// <summary>
    /// The operation failed.
    /// </summary>
    Error,

    /// <summary>
    /// The operation stopped after a user cancellation request.
    /// </summary>
    Cancelled
}
