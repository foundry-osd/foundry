// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Files;
using Serilog;

namespace Foundry.Core.Services.Configuration;

/// <summary>
/// Serializes authoring documents and migrates loaded ones, importing files named by legacy path members into the library.
/// </summary>
/// <param name="files">The authoring file library receiving legacy files; without one, legacy paths become null references.</param>
public sealed class FoundryConfigurationService(AuthoringFileLibrary? files = null) : IFoundryConfigurationService
{
    public string Serialize(FoundryConfigurationDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return JsonSerializer.Serialize(document, ConfigurationJsonDefaults.SerializerOptions);
    }

    public FoundryConfigurationDocument Deserialize(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        JsonNode? root = JsonNode.Parse(json);
        LegacyFilePaths? legacyPaths = root is null ? null : FoundryConfigurationMigration.ExtractLegacyFilePaths(root);
        FoundryConfigurationDocument document = root?.Deserialize<FoundryConfigurationDocument>(ConfigurationJsonDefaults.SerializerOptions)
            ?? new FoundryConfigurationDocument();
        document = FoundryConfigurationMigration.ApplySchemaMigrations(document);
        return legacyPaths is null || !HasAny(legacyPaths)
            ? document
            : FoundryConfigurationMigration.ApplyLegacyFilePaths(document, legacyPaths, ImportIfPresent);
    }

    private static bool HasAny(LegacyFilePaths paths) =>
        paths.Dot1xProfileTemplate is not null || paths.Dot1xCertificate is not null ||
        paths.WifiEnterpriseProfileTemplate is not null || paths.WifiCertificate is not null ||
        paths.AutopilotPfx is not null || paths.UnattendSourcePaths.Count > 0;

    private AuthoringFileReference? ImportIfPresent(string path)
    {
        if (files is null) return null;
        try
        {
            if (!File.Exists(path)) return null;
            // Loading runs synchronously at startup; read the bounded file here instead of blocking on the async import.
            using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (input.Length > AuthoringFileLibrary.MaximumFileBytes) throw new InvalidDataException("AuthoringFile.TooLarge");
            byte[] content = new byte[(int)input.Length];
            AuthoringFileReference reference;
            try
            {
                input.ReadExactly(content);
                reference = files.Import(content, Path.GetFileName(path));
            }
            finally
            {
                CryptographicOperations.ZeroMemory(content);
            }
            Log.ForContext<FoundryConfigurationService>().Information("Legacy authoring file imported into the library. ContentHash={ContentHash}", reference.ContentHash);
            return reference;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException or NotSupportedException)
        {
            Log.ForContext<FoundryConfigurationService>().Warning(exception, "A legacy authoring file could not be imported into the library; it must be selected again.");
            return null;
        }
    }
}
