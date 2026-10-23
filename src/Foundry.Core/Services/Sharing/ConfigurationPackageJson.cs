// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Foundry.Core.Services.Sharing;

/// <summary>JSON settings shared by configuration package envelopes, payloads and publication files.</summary>
internal static class ConfigurationPackageJson
{
    /// <summary>Maximum JSON nesting depth accepted when reading a package.</summary>
    public const int MaxDepth = 64;

    /// <summary>
    /// camelCase members and string enums; unknown members are ignored so newer writers can add optional data;
    /// null members are omitted. Frozen so no caller can change the shared instance. The envelope and its signed bytes use
    /// their own options in <see cref="ConfigurationPackageCodec"/>.
    /// </summary>
    public static readonly JsonSerializerOptions SerializerOptions = CreateSerializerOptions();

    private static JsonSerializerOptions CreateSerializerOptions()
    {
        JsonSerializerOptions options = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip,
            MaxDepth = MaxDepth,
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
        };
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }
}
