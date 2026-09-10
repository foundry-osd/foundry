// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Bootstrap.Runtime;

/// <summary>Resolves Connect and Deploy payloads without launching them.</summary>
internal interface IRuntimeResolver
{
    /// <summary>Resolves an executable, preserving explicit override failures and permitted offline fallback.</summary>
    Task<string> ResolveAsync(string applicationName, bool skipReleaseLookup, CancellationToken cancellationToken);
}

/// <summary>Reports measured payload transfer progress; an absent total means the size is unknown.</summary>
internal sealed record RuntimeDownloadProgress(string ApplicationName, long BytesReceived, long? TotalBytes);
