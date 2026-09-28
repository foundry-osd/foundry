// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Security.Cryptography;

namespace Foundry.Core.Services.Configuration;

/// <summary>Owns credential-bearing derived bytes; dispose after validation or staging. Hashes contain no source values.</summary>
public sealed class PreOobeUnattendIntegrationResult : IDisposable
{
    private byte[]? content;
    internal PreOobeUnattendIntegrationResult(byte[] content, string sourceHash, string derivedHash, bool modified)
    {
        this.content = content;
        SourceSha256 = sourceHash;
        DerivedSha256 = derivedHash;
        IsModified = modified;
    }
    public ReadOnlyMemory<byte> DerivedContent => content ?? throw new ObjectDisposedException(nameof(PreOobeUnattendIntegrationResult));
    public string SourceSha256 { get; }
    public string DerivedSha256 { get; }
    public bool IsModified { get; }
    public void Dispose()
    {
        byte[]? bytes = Interlocked.Exchange(ref content, null);
        if (bytes is not null) CryptographicOperations.ZeroMemory(bytes);
    }
}
