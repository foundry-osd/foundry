// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Connect.Security;

/// <summary>Asks the technician for the deployment password. Returns an owned array the caller zeroes, or null when cancelled.</summary>
public interface IDeploymentPasswordPrompt
{
    /// <summary>
    /// Shows the modal password prompt and waits for the technician. Callable from any thread.
    /// </summary>
    /// <param name="previousAttemptFailed">Shows the incorrect password text when the previous attempt was rejected.</param>
    /// <returns>
    /// The typed password, owned by the caller, who zeroes it with <c>CryptographicOperations.ZeroMemory</c> after use;
    /// <see langword="null"/> when the technician cancelled.
    /// </returns>
    char[]? Prompt(bool previousAttemptFailed);
}
