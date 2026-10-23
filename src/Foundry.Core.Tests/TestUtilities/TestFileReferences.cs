// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Security.Cryptography;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Configuration;

namespace Foundry.Core.Tests.TestUtilities;

/// <summary>Builds authoring file references for tests, either detached or bound to a file on disk.</summary>
internal static class TestFileReferences
{
    /// <summary>Returns a well-formed reference that no resolver knows.</summary>
    public static AuthoringFileReference Detached(string fileName) =>
        new() { ContentHash = Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(fileName))), FileName = fileName, Length = 1 };

    /// <summary>Returns the reference of an existing file, named after it.</summary>
    public static AuthoringFileReference For(string path)
    {
        byte[] content = File.ReadAllBytes(path);
        return new AuthoringFileReference
        {
            ContentHash = Convert.ToHexStringLower(SHA256.HashData(content)),
            FileName = Path.GetFileName(path),
            Length = content.Length
        };
    }

    /// <summary>Returns inputs resolving the references of <paramref name="paths"/> to those paths.</summary>
    public static MediaInputs Resolving(params string[] paths)
    {
        Dictionary<string, string> byHash = paths.ToDictionary(path => For(path).ContentHash, path => path, StringComparer.OrdinalIgnoreCase);
        return new MediaInputs { ResolveFile = reference => byHash.GetValueOrDefault(reference.ContentHash) };
    }
}
