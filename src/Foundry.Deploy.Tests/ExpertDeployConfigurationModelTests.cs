using System.Text.Json;
using Foundry.Deploy.Models.Configuration;

namespace Foundry.Deploy.Tests;

public sealed class ExpertDeployConfigurationModelTests
{
    [Fact]
    public void Deserialize_WhenLocalizationIncludesDefaultTimeZoneId_PreservesValue()
    {
        const string json = """
            {
              "schemaVersion": 2,
              "localization": {
                "defaultTimeZoneId": "Romance Standard Time"
              }
            }
            """;

        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        FoundryDeployConfigurationDocument? document = JsonSerializer.Deserialize<FoundryDeployConfigurationDocument>(json, options);

        Assert.NotNull(document);
        Assert.Equal("Romance Standard Time", document.Localization.DefaultTimeZoneId);
    }
}
