// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Text;
using System.Text.Json;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Autopilot;

namespace Foundry.Core.Services.Configuration;

public sealed class AutopilotProfileImportService : IAutopilotProfileImportService
{
    private const string ProfileFileName = "AutopilotConfigurationFile.json";

    public async Task<AutopilotProfileSettings> ImportFromJsonFileAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        await using var input = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        if (input.Length > AutopilotOfflineProfileValidator.MaximumContentLength)
            throw new InvalidDataException("The offline Autopilot profile exceeds its size limit.");
        byte[] bytes = new byte[(int)input.Length];
        await input.ReadExactlyAsync(bytes, cancellationToken).ConfigureAwait(false);
        if (await input.ReadAsync(new byte[1], cancellationToken).ConfigureAwait(false) != 0)
            throw new InvalidDataException("The offline Autopilot profile changed while it was read.");
        AutopilotOfflineProfileValidator.Validate(bytes);
        string jsonContent = Encoding.ASCII.GetString(bytes);
        using JsonDocument document = JsonDocument.Parse(jsonContent);
        string displayName = ResolveDisplayName(document.RootElement, filePath);
        string id = AutopilotProfileSettingsFactory.BuildManualProfileId(jsonContent);

        return AutopilotProfileSettingsFactory.Create(id, displayName, jsonContent, "Manual import", DateTimeOffset.UtcNow);
    }

    private static string ResolveDisplayName(JsonElement rootElement, string filePath)
    {
        if (rootElement.TryGetProperty("Comment_File", out JsonElement commentProperty) &&
            commentProperty.ValueKind == JsonValueKind.String)
        {
            string? comment = commentProperty.GetString();
            if (!string.IsNullOrWhiteSpace(comment))
            {
                return comment.Trim();
            }
        }

        string fileName = Path.GetFileNameWithoutExtension(filePath);
        if (fileName.Equals(ProfileFileName[..^5], StringComparison.OrdinalIgnoreCase))
        {
            string? parentDirectoryName = Path.GetFileName(Path.GetDirectoryName(filePath));
            if (!string.IsNullOrWhiteSpace(parentDirectoryName))
            {
                return parentDirectoryName;
            }
        }

        return fileName;
    }

}
