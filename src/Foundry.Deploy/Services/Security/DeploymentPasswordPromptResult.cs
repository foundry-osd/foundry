// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace Foundry.Deploy.Services.Security;

public sealed class DeploymentPasswordPromptResult : IDisposable
{
    private DeploymentPasswordPromptResult(bool wasSubmitted, char[] password)
    {
        WasSubmitted = wasSubmitted;
        Password = password;
    }

    public bool WasSubmitted { get; }

    public char[] Password { get; }

    public static DeploymentPasswordPromptResult Submitted(ReadOnlySpan<char> password) =>
        new(wasSubmitted: true, password.ToArray());

    internal static DeploymentPasswordPromptResult SubmittedOwned(char[] password) =>
        new(wasSubmitted: true, password);

    public static DeploymentPasswordPromptResult Cancelled() =>
        new(wasSubmitted: false, []);

    public void Dispose()
    {
        CryptographicOperations.ZeroMemory(MemoryMarshal.AsBytes(Password.AsSpan()));
    }
}
