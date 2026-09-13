// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Utilities.Security;

/// <summary>
/// Stores generic credentials for the executing Windows user on the current computer.
/// Callers own case-insensitive target identity and policy; native failures are never treated as missing secrets.
/// </summary>
public interface IWindowsCredentialStore
{
    /// <summary>
    /// Reads an owned secret buffer, or returns null only when the target does not exist.
    /// The caller must dispose the returned credential.
    /// </summary>
    WindowsCredential? Read(string target);

    /// <summary>
    /// Copies and persists an opaque secret, preserving empty values and optional username metadata.
    /// </summary>
    void Write(string target, ReadOnlySpan<byte> secret, string? userName = null);

    /// <summary>
    /// Deletes the target. A missing target is already deleted; other native failures are reported.
    /// </summary>
    void Delete(string target);
}
