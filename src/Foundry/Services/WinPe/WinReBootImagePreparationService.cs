using System.Globalization;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;

namespace Foundry.Services.WinPe;

internal sealed class WinReBootImagePreparationService : IWinReBootImagePreparationService
{
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromMinutes(30)
    };

    private readonly WinPeProcessRunner _processRunner;
    private readonly ILogger<WinReBootImagePreparationService> _logger;

    public WinReBootImagePreparationService(
        WinPeProcessRunner processRunner,
        ILogger<WinReBootImagePreparationService> logger)
    {
        _processRunner = processRunner;
        _logger = logger;
    }

    public async Task<WinPeResult> ReplaceBootWimAsync(
        WinPeBuildArtifact artifact,
        WinPeToolPaths tools,
        string winPeLanguage,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        ArgumentNullException.ThrowIfNull(tools);

        string normalizedLanguage = WinPeLanguageUtility.Normalize(winPeLanguage);
        WinPeResult<IReadOnlyList<WinReSourceCandidate>> selection = await SelectCatalogCandidatesAsync(
            artifact.Architecture,
            normalizedLanguage,
            cancellationToken).ConfigureAwait(false);
        if (!selection.IsSuccess)
        {
            return WinPeResult.Failure(selection.Error!);
        }

        List<string> diagnostics = [];
        foreach (WinReSourceCandidate candidate in selection.Value!)
        {
            _logger.LogInformation(
                "Preparing WinRE boot image source. RequestedEdition={RequestedEdition}, ReleaseId={ReleaseId}, Architecture={Architecture}, LanguageCode={LanguageCode}, CatalogEdition={CatalogEdition}, ClientType={ClientType}, LicenseChannel={LicenseChannel}, FileName={FileName}",
                candidate.RequestedEdition,
                candidate.Source.ReleaseId,
                candidate.Source.Architecture,
                candidate.Source.LanguageCode,
                candidate.Source.Edition,
                candidate.Source.ClientType,
                candidate.Source.LicenseChannel,
                candidate.Source.FileName);

            WinPeResult attempt = await TryReplaceBootWimFromSourceAsync(
                artifact,
                tools,
                candidate,
                cancellationToken).ConfigureAwait(false);
            if (attempt.IsSuccess)
            {
                return attempt;
            }

            string attemptDetails = string.IsNullOrWhiteSpace(attempt.Error?.Details)
                ? string.Empty
                : $"{Environment.NewLine}{attempt.Error!.Details}";
            diagnostics.Add($"{candidate.RequestedEdition}: {attempt.Error?.Message}{attemptDetails}");
            _logger.LogWarning(
                "WinRE boot image preparation attempt failed. RequestedEdition={RequestedEdition}, ErrorCode={ErrorCode}, ErrorMessage={ErrorMessage}",
                candidate.RequestedEdition,
                attempt.Error?.Code,
                attempt.Error?.Message);
        }

        return WinPeResult.Failure(
            WinPeErrorCodes.WinReExtractionFailed,
            "Failed to prepare a WinRE boot image from the supported operating system sources.",
            string.Join($"{Environment.NewLine}{Environment.NewLine}", diagnostics));
    }

    private async Task<WinPeResult<IReadOnlyList<WinReSourceCandidate>>> SelectCatalogCandidatesAsync(
        WinPeArchitecture architecture,
        string normalizedLanguage,
        CancellationToken cancellationToken)
    {
        XDocument document;
        try
        {
            string xml = await HttpClient.GetStringAsync(WinPeDefaults.DefaultOperatingSystemCatalogUri, cancellationToken).ConfigureAwait(false);
            document = XDocument.Parse(xml);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load operating system catalog for WinRE preparation.");
            return WinPeResult<IReadOnlyList<WinReSourceCandidate>>.Failure(
                WinPeErrorCodes.OperatingSystemCatalogFetchFailed,
                "Failed to load the operating system catalog for WinRE preparation.",
                ex.Message);
        }

        WinReCatalogItem[] items;
        try
        {
            items = document
                .Descendants("Item")
                .Select(ParseCatalogItem)
                .Where(item => !string.IsNullOrWhiteSpace(item.Url))
                .ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse operating system catalog for WinRE preparation.");
            return WinPeResult<IReadOnlyList<WinReSourceCandidate>>.Failure(
                WinPeErrorCodes.OperatingSystemCatalogParseFailed,
                "Failed to parse the operating system catalog for WinRE preparation.",
                ex.Message);
        }

        string requiredArchitecture = NormalizeArchitecture(architecture);
        WinReCatalogItem[] matchingItems = items
            .Where(item => item.WindowsRelease.Equals("11", StringComparison.OrdinalIgnoreCase))
            .Where(item => item.ReleaseId.Equals("24H2", StringComparison.OrdinalIgnoreCase))
            .Where(item => item.Architecture.Equals(requiredArchitecture, StringComparison.OrdinalIgnoreCase))
            .Where(item => WinPeLanguageUtility.Normalize(item.LanguageCode).Equals(normalizedLanguage, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        List<WinReSourceCandidate> candidates = [];
        WinReCatalogItem? proSource = SelectPreferredSourceItem(matchingItems, "CLIENTCONSUMER");
        if (proSource is not null)
        {
            candidates.Add(new WinReSourceCandidate
            {
                RequestedEdition = "Pro",
                Source = proSource
            });
        }

        WinReCatalogItem? enterpriseSource = SelectPreferredSourceItem(matchingItems, "CLIENTBUSINESS");
        if (enterpriseSource is not null &&
            !candidates.Any(item => item.Source.Url.Equals(enterpriseSource.Url, StringComparison.OrdinalIgnoreCase)))
        {
            candidates.Add(new WinReSourceCandidate
            {
                RequestedEdition = "Enterprise",
                Source = enterpriseSource
            });
        }

        if (candidates.Count == 0)
        {
            return WinPeResult<IReadOnlyList<WinReSourceCandidate>>.Failure(
                WinPeErrorCodes.WinReSourceSelectionFailed,
                "No WinRE source media matched the selected language and architecture.",
                $"Expected Windows 11 24H2 media for Language='{normalizedLanguage}', Architecture='{requiredArchitecture}', ClientType in [CLIENTCONSUMER, CLIENTBUSINESS].");
        }

        return WinPeResult<IReadOnlyList<WinReSourceCandidate>>.Success(candidates);
    }

    private static WinReCatalogItem ParseCatalogItem(XElement item)
    {
        return new WinReCatalogItem
        {
            WindowsRelease = ReadElement(item, "windowsRelease"),
            ReleaseId = ReadElement(item, "releaseId"),
            BuildMajor = ParseInt(ReadElement(item, "buildMajor")),
            BuildUbr = ParseInt(ReadElement(item, "buildUbr")),
            Architecture = NormalizeArchitecture(ReadElement(item, "architecture")),
            LanguageCode = ReadElement(item, "languageCode"),
            Edition = ReadElement(item, "edition"),
            ClientType = ReadElement(item, "clientType"),
            LicenseChannel = ReadElement(item, "licenseChannel"),
            FileName = ReadElement(item, "fileName"),
            Url = ReadElement(item, "url"),
            Sha256 = ReadElement(item, "sha256")
        };
    }

    private async Task<WinPeResult> EnsureDownloadedAsync(
        string sourceUrl,
        string destinationPath,
        string? expectedSha256,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        try
        {
            if (File.Exists(destinationPath) &&
                await ValidateHashIfRequestedAsync(destinationPath, expectedSha256, cancellationToken).ConfigureAwait(false))
            {
                return WinPeResult.Success();
            }

            string effectiveSourceUrl = NormalizeSourceUrl(sourceUrl);
            using HttpResponseMessage response = await HttpClient
                .GetAsync(effectiveSourceUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return WinPeResult.Failure(
                    WinPeErrorCodes.DownloadFailed,
                    "Failed to download the selected operating system image.",
                    $"SourceUrl='{effectiveSourceUrl}', Status={(int)response.StatusCode} {response.ReasonPhrase}");
            }

            await using Stream sourceStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            string? directory = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await using FileStream destinationStream = new(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
            await sourceStream.CopyToAsync(destinationStream, cancellationToken).ConfigureAwait(false);

            if (!await ValidateHashIfRequestedAsync(destinationPath, expectedSha256, cancellationToken).ConfigureAwait(false))
            {
                return WinPeResult.Failure(
                    WinPeErrorCodes.HashMismatch,
                    "Downloaded operating system image failed hash validation.",
                    $"Path='{destinationPath}'.");
            }

            return WinPeResult.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to download operating system image for WinRE preparation. DestinationPath={DestinationPath}", destinationPath);
            return WinPeResult.Failure(
                WinPeErrorCodes.DownloadFailed,
                "Failed to download the selected operating system image.",
                ex.Message);
        }
    }

    private async Task<WinPeResult> TryReplaceBootWimFromSourceAsync(
        WinPeBuildArtifact artifact,
        WinPeToolPaths tools,
        WinReSourceCandidate candidate,
        CancellationToken cancellationToken)
    {
        string cacheRoot = Path.Combine(WinPeDefaults.GetInstallerCacheDirectoryPath(), "os");
        Directory.CreateDirectory(cacheRoot);

        string downloadFileName = string.IsNullOrWhiteSpace(candidate.Source.FileName)
            ? $"{candidate.Source.ReleaseId}-{candidate.Source.Architecture}-{candidate.Source.LanguageCode}.esd"
            : WinPeFileSystemHelper.SanitizePathSegment(candidate.Source.FileName);
        string esdPath = Path.Combine(cacheRoot, downloadFileName);

        WinPeResult download = await EnsureDownloadedAsync(
            candidate.Source.Url,
            esdPath,
            candidate.Source.Sha256,
            artifact.WorkingDirectoryPath,
            cancellationToken).ConfigureAwait(false);
        if (!download.IsSuccess)
        {
            return download;
        }

        WinPeResult<int> imageIndex = await ResolveImageIndexAsync(
            tools.DismPath,
            esdPath,
            candidate.RequestedEdition,
            artifact.WorkingDirectoryPath,
            cancellationToken).ConfigureAwait(false);
        if (!imageIndex.IsSuccess)
        {
            return WinPeResult.Failure(imageIndex.Error!);
        }

        string sourceWorkspaceName = NormalizeToken(candidate.RequestedEdition);
        string winReWorkspace = Path.Combine(artifact.WorkingDirectoryPath, $"winre-source-{sourceWorkspaceName}");
        string exportDirectory = Path.Combine(winReWorkspace, "export");
        string installMountPath = Path.Combine(winReWorkspace, "install-mount");
        string installWimPath = Path.Combine(exportDirectory, "install.wim");
        WinPeFileSystemHelper.EnsureDirectoryClean(exportDirectory);
        WinPeFileSystemHelper.EnsureDirectoryClean(installMountPath);

        try
        {
            WinPeProcessExecution exportExecution = await _processRunner.RunAsync(
                tools.DismPath,
                $"/Export-Image /SourceImageFile:{WinPeProcessRunner.Quote(esdPath)} /SourceIndex:{imageIndex.Value} /DestinationImageFile:{WinPeProcessRunner.Quote(installWimPath)} /Compress:max /CheckIntegrity",
                artifact.WorkingDirectoryPath,
                cancellationToken).ConfigureAwait(false);
            if (!exportExecution.IsSuccess || !File.Exists(installWimPath))
            {
                return WinPeResult.Failure(
                    WinPeErrorCodes.WinReExtractionFailed,
                    "Failed to export the selected operating system image for WinRE extraction.",
                    exportExecution.ToDiagnosticText());
            }

            WinPeResult<WinPeMountSession> mount = await WinPeMountSession.MountAsync(
                _processRunner,
                tools.DismPath,
                installWimPath,
                installMountPath,
                artifact.WorkingDirectoryPath,
                cancellationToken).ConfigureAwait(false);
            if (!mount.IsSuccess)
            {
                return WinPeResult.Failure(
                    WinPeErrorCodes.WinReExtractionFailed,
                    "Failed to mount the exported operating system image for WinRE extraction.",
                    mount.Error?.Details);
            }

            await using WinPeMountSession session = mount.Value!;
            string sourceWinRePath = Path.Combine(session.MountDirectoryPath, "Windows", "System32", "Recovery", "winre.wim");
            if (!File.Exists(sourceWinRePath))
            {
                return await FailWithDiscardAsync(
                    new WinPeDiagnostic(
                        WinPeErrorCodes.WinReExtractionFailed,
                        "The selected operating system image does not contain winre.wim.",
                        $"Expected path: '{sourceWinRePath}'."),
                    session,
                    cancellationToken).ConfigureAwait(false);
            }

            File.Copy(sourceWinRePath, artifact.BootWimPath, overwrite: true);
            WinPeResult discard = await session.DiscardAsync(cancellationToken).ConfigureAwait(false);
            if (!discard.IsSuccess)
            {
                return discard;
            }

            _logger.LogInformation(
                "Replaced WinPE boot.wim with extracted winre.wim successfully. RequestedEdition={RequestedEdition}, BootWimPath={BootWimPath}, SourceWinRePath={SourceWinRePath}",
                candidate.RequestedEdition,
                artifact.BootWimPath,
                sourceWinRePath);
            return WinPeResult.Success();
        }
        finally
        {
            TryDeleteDirectory(installMountPath);
            TryDeleteDirectory(exportDirectory);
        }
    }

    private async Task<WinPeResult<int>> ResolveImageIndexAsync(
        string dismPath,
        string imagePath,
        string requestedEdition,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        WinPeProcessExecution execution = await _processRunner.RunAsync(
            dismPath,
            $"/English /Get-ImageInfo /ImageFile:{WinPeProcessRunner.Quote(imagePath)}",
            workingDirectory,
            cancellationToken).ConfigureAwait(false);
        if (!execution.IsSuccess)
        {
            return WinPeResult<int>.Failure(
                WinPeErrorCodes.WinReIndexResolutionFailed,
                "Failed to inspect the selected operating system image.",
                execution.ToDiagnosticText());
        }

        IReadOnlyList<ImageIndexDescriptor> descriptors = ParseImageDescriptors(execution.StandardOutput);
        if (descriptors.Count == 0)
        {
            return WinPeResult<int>.Failure(
                WinPeErrorCodes.WinReIndexResolutionFailed,
                "The selected operating system image did not expose any install indexes.",
                execution.StandardOutput);
        }

        string requested = NormalizeToken(requestedEdition);
        ImageIndexDescriptor? bestMatch = descriptors.FirstOrDefault(item =>
            ContainsNormalized(item.Name, requested) ||
            ContainsNormalized(item.Edition, requested) ||
            ContainsNormalized(item.EditionId, requested));
        if (bestMatch is null)
        {
            return WinPeResult<int>.Failure(
                WinPeErrorCodes.WinReIndexResolutionFailed,
                "Failed to resolve the requested operating system edition within the downloaded image.",
                $"RequestedEdition='{requestedEdition}'.");
        }

        return WinPeResult<int>.Success(bestMatch.Index);
    }

    private static async Task<bool> ValidateHashIfRequestedAsync(string filePath, string? expectedHash, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(expectedHash))
        {
            return true;
        }

        string normalized = expectedHash.Trim().Replace("-", string.Empty, StringComparison.OrdinalIgnoreCase).ToUpperInvariant();
        if (normalized.Length != 64)
        {
            return true;
        }

        using SHA256 sha256 = SHA256.Create();
        await using FileStream stream = new(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        byte[] hash = await sha256.ComputeHashAsync(stream, cancellationToken).ConfigureAwait(false);
        string actual = Convert.ToHexString(hash);
        return normalized.Equals(actual, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeSourceUrl(string sourceUrl)
    {
        if (!Uri.TryCreate(sourceUrl, UriKind.Absolute, out Uri? uri))
        {
            return sourceUrl;
        }

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return sourceUrl;
        }

        if (!uri.Host.Equals("dl.delivery.mp.microsoft.com", StringComparison.OrdinalIgnoreCase) &&
            !uri.Host.EndsWith(".dl.delivery.mp.microsoft.com", StringComparison.OrdinalIgnoreCase))
        {
            return sourceUrl;
        }

        UriBuilder builder = new(uri)
        {
            Scheme = Uri.UriSchemeHttp,
            Port = uri.Port == 443 ? 80 : uri.Port
        };

        return builder.Uri.AbsoluteUri;
    }

    private static async Task<WinPeResult> FailWithDiscardAsync(
        WinPeDiagnostic primaryDiagnostic,
        WinPeMountSession session,
        CancellationToken cancellationToken)
    {
        WinPeResult discardResult = await session.DiscardAsync(cancellationToken).ConfigureAwait(false);
        if (discardResult.IsSuccess)
        {
            return WinPeResult.Failure(primaryDiagnostic);
        }

        string details = string.Join(
            Environment.NewLine,
            primaryDiagnostic.Details ?? string.Empty,
            "Discard diagnostics:",
            discardResult.Error?.Details ?? string.Empty).Trim();

        return WinPeResult.Failure(new WinPeDiagnostic(
            primaryDiagnostic.Code,
            primaryDiagnostic.Message,
            details));
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

    private static string NormalizeArchitecture(WinPeArchitecture architecture)
    {
        return architecture switch
        {
            WinPeArchitecture.X64 => "x64",
            WinPeArchitecture.Arm64 => "arm64",
            _ => architecture.ToString().ToLowerInvariant()
        };
    }

    private static WinReCatalogItem? SelectPreferredSourceItem(
        IEnumerable<WinReCatalogItem> items,
        string clientType)
    {
        return items
            .Where(item => item.ClientType.Equals(clientType, StringComparison.OrdinalIgnoreCase))
            .OrderBy(item => GetLicenseChannelOrder(item.LicenseChannel))
            .ThenBy(item => GetClientTypeOrder(item.ClientType))
            .ThenByDescending(item => item.BuildMajor)
            .ThenByDescending(item => item.BuildUbr)
            .FirstOrDefault();
    }

    private static int GetLicenseChannelOrder(string licenseChannel)
    {
        return licenseChannel.Equals("RET", StringComparison.OrdinalIgnoreCase)
            ? 0
            : licenseChannel.Equals("VOL", StringComparison.OrdinalIgnoreCase)
                ? 1
                : 99;
    }

    private static int GetClientTypeOrder(string clientType)
    {
        return clientType.Equals("CLIENTCONSUMER", StringComparison.OrdinalIgnoreCase)
            ? 0
            : clientType.Equals("CLIENTBUSINESS", StringComparison.OrdinalIgnoreCase)
                ? 1
                : 99;
    }

    private static IReadOnlyList<ImageIndexDescriptor> ParseImageDescriptors(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return [];
        }

        var descriptors = new List<ImageIndexDescriptor>();
        ImageIndexDescriptor? current = null;

        foreach (string line in output.Split(["\r\n", "\n"], StringSplitOptions.None))
        {
            Match indexMatch = Regex.Match(line, @"^\s*Index\s*:\s*(\d+)\s*$", RegexOptions.IgnoreCase);
            if (indexMatch.Success)
            {
                if (current is not null)
                {
                    descriptors.Add(current);
                }

                current = new ImageIndexDescriptor
                {
                    Index = int.Parse(indexMatch.Groups[1].Value, CultureInfo.InvariantCulture),
                    Name = string.Empty,
                    Edition = string.Empty,
                    EditionId = string.Empty
                };

                continue;
            }

            if (current is null)
            {
                continue;
            }

            Match nameMatch = Regex.Match(line, @"^\s*Name\s*:\s*(.+)\s*$", RegexOptions.IgnoreCase);
            if (nameMatch.Success)
            {
                current = current with { Name = nameMatch.Groups[1].Value.Trim() };
                continue;
            }

            Match editionMatch = Regex.Match(line, @"^\s*Edition\s*:\s*(.+)\s*$", RegexOptions.IgnoreCase);
            if (editionMatch.Success)
            {
                current = current with { Edition = editionMatch.Groups[1].Value.Trim() };
                continue;
            }

            Match editionIdMatch = Regex.Match(line, @"^\s*Edition\s+ID\s*:\s*(.+)\s*$", RegexOptions.IgnoreCase);
            if (editionIdMatch.Success)
            {
                current = current with { EditionId = editionIdMatch.Groups[1].Value.Trim() };
            }
        }

        if (current is not null)
        {
            descriptors.Add(current);
        }

        return descriptors;
    }

    private static bool ContainsNormalized(string source, string expected)
    {
        string normalized = NormalizeToken(source);
        if (normalized.Length == 0 || expected.Length == 0)
        {
            return false;
        }

        return normalized.Contains(expected, StringComparison.OrdinalIgnoreCase) ||
               expected.Contains(normalized, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeToken(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        char[] filtered = value
            .ToLowerInvariant()
            .Where(char.IsLetterOrDigit)
            .ToArray();

        return new string(filtered);
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // Best-effort cleanup.
        }
    }

    private sealed record WinReCatalogItem
    {
        public required string WindowsRelease { get; init; }
        public required string ReleaseId { get; init; }
        public required int BuildMajor { get; init; }
        public required int BuildUbr { get; init; }
        public required string Architecture { get; init; }
        public required string LanguageCode { get; init; }
        public required string Edition { get; init; }
        public required string ClientType { get; init; }
        public required string LicenseChannel { get; init; }
        public required string FileName { get; init; }
        public required string Url { get; init; }
        public required string Sha256 { get; init; }
    }

    private sealed record ImageIndexDescriptor
    {
        public required int Index { get; init; }
        public required string Name { get; init; }
        public required string Edition { get; init; }
        public required string EditionId { get; init; }
    }

    private sealed record WinReSourceCandidate
    {
        public required string RequestedEdition { get; init; }
        public required WinReCatalogItem Source { get; init; }
    }
}
