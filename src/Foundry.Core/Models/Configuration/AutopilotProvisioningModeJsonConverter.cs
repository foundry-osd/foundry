using System.Text.Json;
using System.Text.Json.Serialization;

namespace Foundry.Core.Models.Configuration;

/// <summary>
/// Preserves readable Autopilot provisioning mode values without changing every enum in shared configuration JSON.
/// </summary>
public sealed class AutopilotProvisioningModeJsonConverter : JsonConverter<AutopilotProvisioningMode>
{
    /// <inheritdoc />
    public override AutopilotProvisioningMode Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out int numericValue))
        {
            return Enum.IsDefined(typeof(AutopilotProvisioningMode), numericValue)
                ? (AutopilotProvisioningMode)numericValue
                : throw new JsonException($"Unsupported Autopilot provisioning mode value '{numericValue}'.");
        }

        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException("Autopilot provisioning mode must be a string or numeric enum value.");
        }

        return reader.GetString() switch
        {
            "jsonProfile" or "JsonProfile" => AutopilotProvisioningMode.JsonProfile,
            "hardwareHashUpload" or "HardwareHashUpload" => AutopilotProvisioningMode.HardwareHashUpload,
            string value => throw new JsonException($"Unsupported Autopilot provisioning mode value '{value}'."),
            null => throw new JsonException("Autopilot provisioning mode cannot be null.")
        };
    }

    /// <inheritdoc />
    public override void Write(
        Utf8JsonWriter writer,
        AutopilotProvisioningMode value,
        JsonSerializerOptions options)
    {
        writer.WriteStringValue(value switch
        {
            AutopilotProvisioningMode.JsonProfile => "jsonProfile",
            AutopilotProvisioningMode.HardwareHashUpload => "hardwareHashUpload",
            _ => throw new JsonException($"Unsupported Autopilot provisioning mode value '{value}'.")
        });
    }
}
