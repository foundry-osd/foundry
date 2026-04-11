using System.Globalization;
using System.Net.Http;
using System.Xml.Linq;
using Foundry.Deploy.Models;
using Foundry.Deploy.Services.Http;
using Microsoft.Extensions.Logging;

namespace Foundry.Deploy.Services.Catalog;

public sealed class DriverPackCatalogService : IDriverPackCatalogService
{
    private const string CatalogUri = "https://raw.githubusercontent.com/foundry-osd/catalog/refs/heads/main/Cache/DriverPack/DriverPack_Unified.xml";
    private static readonly HttpClient HttpClient = InsecureHttpClientFactory.Create(TimeSpan.FromMinutes(60));
    private readonly ILogger<DriverPackCatalogService> _logger;

    public DriverPackCatalogService(ILogger<DriverPackCatalogService> logger)
    {
        _logger = logger;
    }

    public async Task<IReadOnlyList<DriverPackCatalogItem>> GetCatalogAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Fetching driver pack catalog from {CatalogUri}.", CatalogUri);
        try
        {
            string xmlContent = await HttpTextFetcher
                .GetStringWithRetryAsync(
                    HttpClient,
                    CatalogUri,
                    _logger,
                    "Driver pack catalog download",
                    cancellationToken)
                .ConfigureAwait(false);
            XDocument document = XDocument.Parse(xmlContent);

            DriverPackCatalogItem[] items = document
                .Descendants("DriverPack")
                .Select(ParseItem)
                .Where(item => !string.IsNullOrWhiteSpace(item.DownloadUrl))
                .OrderByDescending(item => item.ReleaseDate ?? DateTimeOffset.MinValue)
                .ThenBy(item => item.Manufacturer, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            _logger.LogInformation("Loaded {ItemCount} driver pack catalog entries.", items.Length);
            return items;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Driver pack catalog load failed from {CatalogUri}.", CatalogUri);
            throw;
        }
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
            OsReleaseId = ReadAttribute(osInfo, "releaseId"),
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
