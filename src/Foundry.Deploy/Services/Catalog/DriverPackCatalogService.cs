using System.Globalization;
using System.Net.Http;
using System.Xml.Linq;
using Foundry.Deploy.Models;

namespace Foundry.Deploy.Services.Catalog;

public sealed class DriverPackCatalogService : IDriverPackCatalogService
{
    private const string CatalogUri = "https://raw.githubusercontent.com/mchave3/Foundry.Automation/refs/heads/main/Cache/DriverPack/DriverPack_Unified.xml";
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromMinutes(5)
    };

    public async Task<IReadOnlyList<DriverPackCatalogItem>> GetCatalogAsync(CancellationToken cancellationToken = default)
    {
        string xmlContent = await HttpClient.GetStringAsync(CatalogUri, cancellationToken).ConfigureAwait(false);
        XDocument document = XDocument.Parse(xmlContent);

        DriverPackCatalogItem[] items = document
            .Descendants("DriverPack")
            .Select(ParseItem)
            .Where(item => !string.IsNullOrWhiteSpace(item.DownloadUrl))
            .OrderByDescending(item => item.ReleaseDate ?? DateTimeOffset.MinValue)
            .ThenBy(item => item.Manufacturer, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return items;
    }

    private static DriverPackCatalogItem ParseItem(XElement driverPack)
    {
        XElement? osInfo = driverPack.Element("OsInfo");
        XElement? hashes = driverPack.Element("Hashes");

        IReadOnlyList<string> models = driverPack
            .Descendants("Model")
            .Select(model => (model.Attribute("name")?.Value ?? string.Empty).Trim())
            .Where(modelName => !string.IsNullOrWhiteSpace(modelName))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new DriverPackCatalogItem
        {
            Id = ReadAttribute(driverPack, "id"),
            PackageId = ReadAttribute(driverPack, "packageId"),
            Manufacturer = ReadAttribute(driverPack, "manufacturer"),
            Name = ReadAttribute(driverPack, "name"),
            Version = ReadAttribute(driverPack, "version"),
            FileName = ReadAttribute(driverPack, "fileName"),
            DownloadUrl = ReadAttribute(driverPack, "downloadUrl"),
            SizeBytes = ParseLong(ReadAttribute(driverPack, "sizeBytes")),
            Format = ReadAttribute(driverPack, "format"),
            Type = ReadAttribute(driverPack, "type"),
            ReleaseDate = ParseDate(ReadAttribute(driverPack, "releaseDate")),
            OsName = ReadAttribute(osInfo, "name"),
            OsArchitecture = NormalizeArchitecture(ReadAttribute(osInfo, "architecture")),
            ModelNames = models,
            Sha256 = ReadAttribute(hashes, "sha256")
        };
    }

    private static string ReadAttribute(XElement? element, string attributeName)
    {
        return (element?.Attribute(attributeName)?.Value ?? string.Empty).Trim();
    }

    private static long ParseLong(string value)
    {
        return long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsed)
            ? parsed
            : 0;
    }

    private static DateTimeOffset? ParseDate(string value)
    {
        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset parsed))
        {
            return parsed;
        }

        return null;
    }

    private static string NormalizeArchitecture(string value)
    {
        string normalized = value.Trim().ToLowerInvariant();
        return normalized switch
        {
            "amd64" => "x64",
            "aarch64" => "arm64",
            _ => normalized
        };
    }
}
