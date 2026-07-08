// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Xml.Linq;

namespace Foundry.Core.Services.WinPe;

/// <inheritdoc />
public sealed class PowerShellGalleryModuleSearchService : IPowerShellGalleryModuleSearchService
{
    private const string SearchUriFormat =
        "https://www.powershellgallery.com/api/v2/Search()?$filter=IsLatestVersion&searchTerm='{0}'&includePrerelease=false&$orderby=DownloadCount%20desc&$top={1}";

    private static readonly XNamespace AtomNamespace = "http://www.w3.org/2005/Atom";
    private static readonly XNamespace DataServicesNamespace = "http://schemas.microsoft.com/ado/2007/08/dataservices";

    private readonly HttpClient _httpClient;

    /// <summary>
    /// Initializes the service with a default HTTP client.
    /// </summary>
    public PowerShellGalleryModuleSearchService()
        : this(new HttpClient())
    {
    }

    internal PowerShellGalleryModuleSearchService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    /// <inheritdoc />
    public async Task<WinPeResult<IReadOnlyList<PowerShellGalleryModule>>> SearchAsync(
        string searchTerm,
        int count = 20,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(searchTerm))
        {
            return WinPeResult<IReadOnlyList<PowerShellGalleryModule>>.Success([]);
        }

        int normalizedCount = Math.Clamp(count, 1, 100);
        string requestUri = string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            SearchUriFormat,
            Uri.EscapeDataString(searchTerm.Trim()),
            normalizedCount);

        try
        {
            using HttpResponseMessage response = await _httpClient
                .GetAsync(requestUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            await using Stream contentStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            XDocument document = await XDocument.LoadAsync(contentStream, LoadOptions.None, cancellationToken).ConfigureAwait(false);

            return WinPeResult<IReadOnlyList<PowerShellGalleryModule>>.Success(ParseModules(document));
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Xml.XmlException or IOException or TaskCanceledException)
        {
            return WinPeResult<IReadOnlyList<PowerShellGalleryModule>>.Failure(
                WinPeErrorCodes.DownloadFailed,
                "Failed to search the PowerShell Gallery.",
                ex.Message);
        }
    }

    private static List<PowerShellGalleryModule> ParseModules(XDocument document)
    {
        List<PowerShellGalleryModule> modules = [];
        if (document.Root is null)
        {
            return modules;
        }

        foreach (XElement entry in document.Root.Elements(AtomNamespace + "entry"))
        {
            string name = entry.Element(AtomNamespace + "title")?.Value?.Trim() ?? string.Empty;
            XElement? properties = entry.Descendants().FirstOrDefault(element => element.Name.LocalName == "properties");

            string version = properties?.Element(DataServicesNamespace + "Version")?.Value?.Trim() ?? string.Empty;
            string description = properties?.Element(DataServicesNamespace + "Description")?.Value?.Trim() ?? string.Empty;
            string authors = properties?.Element(DataServicesNamespace + "Authors")?.Value?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(version))
            {
                continue;
            }

            modules.Add(new PowerShellGalleryModule
            {
                Name = name,
                Version = version,
                Authors = authors,
                Description = description
            });
        }

        return modules;
    }
}
