using System.Globalization;
using System.Net.Http;
using System.Xml.Linq;
using Foundry.Deploy.Models;
using Microsoft.Extensions.Logging;

namespace Foundry.Deploy.Services.Catalog;

public sealed class OperatingSystemCatalogService : IOperatingSystemCatalogService
{
    private const string CatalogUri = "https://raw.githubusercontent.com/mchave3/Foundry.Automation/refs/heads/main/Cache/OS/OperatingSystem.xml";
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromMinutes(5)
    };
    private readonly ILogger<OperatingSystemCatalogService> _logger;

    public OperatingSystemCatalogService(ILogger<OperatingSystemCatalogService> logger)
    {
        _logger = logger;
    }

    public async Task<IReadOnlyList<OperatingSystemCatalogItem>> GetCatalogAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Fetching operating system catalog from {CatalogUri}.", CatalogUri);
        try
        {
            string xmlContent = await HttpClient.GetStringAsync(CatalogUri, cancellationToken).ConfigureAwait(false);
            XDocument document = XDocument.Parse(xmlContent);

            OperatingSystemCatalogItem[] items = document
                .Descendants("Item")
                .Select(ParseItem)
                .Where(item => !string.IsNullOrWhiteSpace(item.Url))
                .OrderByDescending(item => item.BuildMajor)
                .ThenByDescending(item => item.BuildUbr)
                .ThenBy(item => item.Architecture, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.LanguageCode, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.Edition, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            _logger.LogInformation("Loaded {ItemCount} operating system catalog entries.", items.Length);
            return items;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Operating system catalog load failed from {CatalogUri}.", CatalogUri);
            throw;
        }
    }

    private static OperatingSystemCatalogItem ParseItem(XElement item)
    {
        return new OperatingSystemCatalogItem
        {
            SourceId = ReadElement(item, "sourceId"),
            ClientType = ReadElement(item, "clientType"),
            WindowsRelease = ReadElement(item, "windowsRelease"),
            ReleaseId = ReadElement(item, "releaseId"),
            Build = ReadElement(item, "build"),
            BuildMajor = ParseInt(ReadElement(item, "buildMajor")),
            BuildUbr = ParseInt(ReadElement(item, "buildUbr")),
            Architecture = NormalizeArchitecture(ReadElement(item, "architecture")),
            LanguageCode = ReadElement(item, "languageCode"),
            Language = ReadElement(item, "language"),
            Edition = ReadElement(item, "edition"),
            FileName = ReadElement(item, "fileName"),
            SizeBytes = ParseLong(ReadElement(item, "sizeBytes")),
            LicenseChannel = ReadElement(item, "licenseChannel"),
            Url = ReadElement(item, "url"),
            Sha1 = ReadElement(item, "sha1"),
            Sha256 = ReadElement(item, "sha256")
        };
    }

    private static string ReadElement(XElement parent, string elementName)
    {
        return (parent.Element(elementName)?.Value ?? string.Empty).Trim();
    }

    private static int ParseInt(string value)
    {
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
            ? parsed
            : 0;
    }

    private static long ParseLong(string value)
    {
        return long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsed)
            ? parsed
            : 0;
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
