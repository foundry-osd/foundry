using System.Globalization;
using System.Net.Http;

namespace Foundry.Services.WinPe;

public sealed class WinPeDriverCatalogService : IWinPeDriverCatalogService
{
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromMinutes(3)
    };

    public async Task<WinPeResult<IReadOnlyList<WinPeDriverCatalogEntry>>> GetCatalogAsync(
        WinPeDriverCatalogOptions options,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        WinPeDiagnostic? validationError = ValidateCatalogOptions(options);
        if (validationError is not null)
        {
            return WinPeResult<IReadOnlyList<WinPeDriverCatalogEntry>>.Failure(validationError);
        }

        string xmlContent;
        try
        {
            if (Uri.TryCreate(options.CatalogUri, UriKind.Absolute, out Uri? uri) &&
                (uri.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase) ||
                 uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase)))
            {
                xmlContent = await HttpClient.GetStringAsync(uri, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                xmlContent = await File.ReadAllTextAsync(options.CatalogUri, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            return WinPeResult<IReadOnlyList<WinPeDriverCatalogEntry>>.Failure(
                WinPeErrorCodes.DriverCatalogFetchFailed,
                "Failed to retrieve the WinPE driver catalog.",
                $"Catalog URI/path: '{options.CatalogUri}'. Error: {ex.Message}");
        }

        try
        {
            XDocument document = XDocument.Parse(xmlContent);
            IReadOnlyList<WinPeDriverCatalogEntry> entries = ParseDriverPacks(document, options);
            return WinPeResult<IReadOnlyList<WinPeDriverCatalogEntry>>.Success(entries);
        }
        catch (Exception ex)
        {
            return WinPeResult<IReadOnlyList<WinPeDriverCatalogEntry>>.Failure(
                WinPeErrorCodes.DriverCatalogParseFailed,
                "Failed to parse the WinPE driver catalog.",
                ex.Message);
        }
    }

    private static IReadOnlyList<WinPeDriverCatalogEntry> ParseDriverPacks(XDocument document, WinPeDriverCatalogOptions options)
    {
        var entries = new List<WinPeDriverCatalogEntry>();
        HashSet<WinPeVendorSelection> requestedVendors = options.Vendors
            .Where(vendor => vendor != WinPeVendorSelection.Any)
            .ToHashSet();

        foreach (XElement pack in document.Descendants("DriverPack"))
        {
            string downloadUrl = (string?)pack.Attribute("downloadUrl") ?? string.Empty;
            if (string.IsNullOrWhiteSpace(downloadUrl))
            {
                continue;
            }

            XElement? osInfo = pack.Element("OsInfo");
            WinPeVendorSelection vendor = ParseVendor((string?)pack.Attribute("manufacturer"));
            if (requestedVendors.Count > 0 && !requestedVendors.Contains(vendor))
            {
                continue;
            }

            string name = (string?)pack.Attribute("name") ?? string.Empty;
            string releaseId = (string?)osInfo?.Attribute("releaseId") ?? string.Empty;
            if (!MatchesRequiredWinPeRelease(releaseId, options.RequiredWinPeReleaseId, name))
            {
                continue;
            }

            string architectureRaw = (string?)osInfo?.Attribute("architecture") ?? string.Empty;
            WinPeArchitecture? architecture = ParseArchitecture(architectureRaw);
            if (architecture is null || architecture.Value != options.Architecture)
            {
                continue;
            }

            string id = (string?)pack.Attribute("id") ?? string.Empty;
            string version = (string?)pack.Attribute("version") ?? string.Empty;
            string fileName = (string?)pack.Attribute("fileName") ?? string.Empty;
            string format = (string?)pack.Attribute("format") ?? string.Empty;
            string sha256 = (string?)pack.Element("Hashes")?.Attribute("sha256") ?? string.Empty;
            DateTimeOffset? releaseDate = TryParseDate((string?)pack.Attribute("releaseDate"));

            if (name.Contains("preview", StringComparison.OrdinalIgnoreCase) ||
                version.Contains("preview", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(options.SearchTerm))
            {
                string term = options.SearchTerm.Trim();
                bool matches =
                    name.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    id.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    version.Contains(term, StringComparison.OrdinalIgnoreCase);

                if (!matches)
                {
                    continue;
                }
            }

            entries.Add(new WinPeDriverCatalogEntry
            {
                Id = id,
                Name = name,
                Version = version,
                Vendor = vendor,
                Architecture = architecture.Value,
                DownloadUri = downloadUrl,
                FileName = fileName,
                Format = format,
                Sha256 = sha256,
                ReleaseDate = releaseDate
            });
        }

        return entries
            .OrderByDescending(entry => entry.ReleaseDate ?? DateTimeOffset.MinValue)
            .ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static WinPeVendorSelection ParseVendor(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return WinPeVendorSelection.Any;
        }

        string normalized = value.Trim();
        if (normalized.Equals("dell", StringComparison.OrdinalIgnoreCase))
        {
            return WinPeVendorSelection.Dell;
        }

        if (normalized.Equals("hp", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("hewlett-packard", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("hewlett packard", StringComparison.OrdinalIgnoreCase))
        {
            return WinPeVendorSelection.Hp;
        }

        if (normalized.Equals("lenovo", StringComparison.OrdinalIgnoreCase))
        {
            return WinPeVendorSelection.Lenovo;
        }

        if (normalized.Equals("microsoft", StringComparison.OrdinalIgnoreCase))
        {
            return WinPeVendorSelection.Microsoft;
        }

        return WinPeVendorSelection.Any;
    }

    private static WinPeArchitecture? ParseArchitecture(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string normalized = value.Trim().ToLowerInvariant();
        return normalized switch
        {
            "x64" or "amd64" => WinPeArchitecture.X64,
            "arm64" or "aarch64" => WinPeArchitecture.Arm64,
            _ => null
        };
    }

    private static DateTimeOffset? TryParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset parsed))
        {
            return parsed;
        }

        return null;
    }

    private static bool MatchesRequiredWinPeRelease(string releaseId, string? requiredReleaseId, string name)
    {
        if (string.IsNullOrWhiteSpace(requiredReleaseId))
        {
            return true;
        }

        string normalizedRequired = requiredReleaseId.Trim();
        if (string.IsNullOrWhiteSpace(normalizedRequired))
        {
            return true;
        }

        string[] releaseTokens = SplitReleaseTokens(releaseId);
        if (releaseTokens.Any(token => token.Equals(normalizedRequired, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return name.Contains($"WinPE{normalizedRequired}", StringComparison.OrdinalIgnoreCase);
    }

    private static string[] SplitReleaseTokens(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Array.Empty<string>();
        }

        return value
            .Split(['/', '\\', ',', ';', '|', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(token => !string.IsNullOrWhiteSpace(token))
            .ToArray();
    }

    private static WinPeDiagnostic? ValidateCatalogOptions(WinPeDriverCatalogOptions? options)
    {
        if (options is null)
        {
            return new WinPeDiagnostic(
                WinPeErrorCodes.ValidationFailed,
                "Driver catalog options are required.",
                "Provide a non-null WinPeDriverCatalogOptions instance.");
        }

        if (string.IsNullOrWhiteSpace(options.CatalogUri))
        {
            return new WinPeDiagnostic(
                WinPeErrorCodes.ValidationFailed,
                "Driver catalog URI is required.",
                "Set WinPeDriverCatalogOptions.CatalogUri.");
        }

        if (!Enum.IsDefined(options.Architecture))
        {
            return new WinPeDiagnostic(
                WinPeErrorCodes.ValidationFailed,
                "Architecture value is invalid.",
                $"Value: '{options.Architecture}'.");
        }

        foreach (WinPeVendorSelection vendor in options.Vendors)
        {
            if (!Enum.IsDefined(vendor))
            {
                return new WinPeDiagnostic(
                    WinPeErrorCodes.ValidationFailed,
                    "Vendor selection value is invalid.",
                    $"Value: '{vendor}'.");
            }
        }

        return null;
    }
}
