using System.Reflection;
using System.Text;
using System.Xml.Linq;
using Foundry.Core.Services.Configuration;

namespace Foundry.Core.Services.WinPe.OsRecovery;

public sealed class OsRecoveryPayloadProvisioningService : IOsRecoveryPayloadProvisioningService
{
    private const string LauncherResourceName = "Foundry.Core.WinRe.FoundryRecoveryLauncher";
    private const string LauncherFileName = "FoundryRecoveryLauncher.cmd";
    private const string WinReConfigFileName = "WinREConfig.xml";
    private static readonly UTF8Encoding Utf8NoBom = new(false);

    private readonly ILanguageRegistryService _languageRegistryService;
    private readonly IWinPeRuntimePayloadProvisioningService _runtimePayloadProvisioningService;

    public OsRecoveryPayloadProvisioningService()
        : this(
            new EmbeddedLanguageRegistryService(),
            new WinPeRuntimePayloadProvisioningService())
    {
    }

    internal OsRecoveryPayloadProvisioningService(
        ILanguageRegistryService languageRegistryService,
        IWinPeRuntimePayloadProvisioningService runtimePayloadProvisioningService)
    {
        _languageRegistryService = languageRegistryService;
        _runtimePayloadProvisioningService = runtimePayloadProvisioningService;
    }

    public async Task<WinPeResult<OsRecoveryPayloadProvisioningResult>> ProvisionAsync(
        OsRecoveryPayloadProvisioningOptions options,
        IProgress<WinPeDownloadProgress>? downloadProgress = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        WinPeDiagnostic? validationError = ValidateOptions(options);
        if (validationError is not null)
        {
            return WinPeResult<OsRecoveryPayloadProvisioningResult>.Failure(validationError);
        }

        try
        {
            string mountedImagePath = Path.GetFullPath(options.MountedImagePath);
            string recoveryToolsPath = Path.Combine(mountedImagePath, "Sources", "Recovery", "Tools");
            string system32Path = Path.Combine(mountedImagePath, "Windows", "System32");
            string foundryConfigPath = Path.Combine(mountedImagePath, "Foundry", "Config");

            Directory.CreateDirectory(recoveryToolsPath);
            Directory.CreateDirectory(system32Path);
            Directory.CreateDirectory(foundryConfigPath);

            string launcherContent = LoadLauncherContent();
            string winReConfigXml = CreateWinReConfigurationXml();
            string bootMenuConfigurationXml = CreateBootMenuConfigurationXml(options.BootMenuLocalizations);

            await File.WriteAllTextAsync(
                Path.Combine(recoveryToolsPath, LauncherFileName),
                launcherContent,
                Utf8NoBom,
                cancellationToken).ConfigureAwait(false);
            await File.WriteAllTextAsync(
                Path.Combine(recoveryToolsPath, WinReConfigFileName),
                winReConfigXml,
                Utf8NoBom,
                cancellationToken).ConfigureAwait(false);
            await File.WriteAllTextAsync(
                Path.Combine(foundryConfigPath, "foundry.connect.config.json"),
                options.FoundryConnectConfigurationJson,
                Utf8NoBom,
                cancellationToken).ConfigureAwait(false);
            await File.WriteAllTextAsync(
                Path.Combine(foundryConfigPath, "foundry.deploy.config.json"),
                options.DeployConfigurationJson,
                Utf8NoBom,
                cancellationToken).ConfigureAwait(false);
            await File.WriteAllTextAsync(
                Path.Combine(foundryConfigPath, "iana-windows-timezones.json"),
                options.IanaWindowsTimeZoneMapJson,
                Utf8NoBom,
                cancellationToken).ConfigureAwait(false);

            File.Copy(options.CurlExecutableSourcePath, Path.Combine(system32Path, "curl.exe"), overwrite: true);

            ProvisionBundledSevenZip(mountedImagePath, options);

            WinPeResult runtimeProvisioningResult = await _runtimePayloadProvisioningService.ProvisionAsync(
                new WinPeRuntimePayloadProvisioningOptions
                {
                    Architecture = options.Architecture,
                    WorkingDirectoryPath = options.WorkingDirectoryPath,
                    MountedImagePath = mountedImagePath,
                    Connect = options.Connect
                },
                downloadProgress,
                cancellationToken).ConfigureAwait(false);

            if (!runtimeProvisioningResult.IsSuccess)
            {
                return WinPeResult<OsRecoveryPayloadProvisioningResult>.Failure(runtimeProvisioningResult.Error!);
            }

            long managedPayloadSizeBytes = CalculateManagedPayloadSizeBytes(mountedImagePath, options.Architecture);
            if (managedPayloadSizeBytes > options.MaxManagedPayloadSizeBytes)
            {
                return WinPeResult<OsRecoveryPayloadProvisioningResult>.Failure(
                    WinPeErrorCodes.ValidationFailed,
                    "Foundry OS recovery managed payload exceeds the default 256 MiB size budget.",
                    $"Managed payload size is {managedPayloadSizeBytes} bytes. Configured budget is {options.MaxManagedPayloadSizeBytes} bytes.");
            }

            return WinPeResult<OsRecoveryPayloadProvisioningResult>.Success(new OsRecoveryPayloadProvisioningResult
            {
                BootMenuConfigurationXml = bootMenuConfigurationXml,
                ManagedPayloadSizeBytes = managedPayloadSizeBytes
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or InvalidOperationException)
        {
            return WinPeResult<OsRecoveryPayloadProvisioningResult>.Failure(
                WinPeErrorCodes.BuildFailed,
                "Failed to provision the Foundry OS recovery payload.",
                ex.Message);
        }
    }

    private string CreateBootMenuConfigurationXml(IReadOnlyList<OsRecoveryBootMenuLocalization> localizations)
    {
        IReadOnlyList<string> supportedCultures = _languageRegistryService
            .GetLanguages()
            .Select(language => LanguageCodeUtility.Canonicalize(language.Code))
            .ToArray();

        Dictionary<string, OsRecoveryBootMenuLocalization> localizationMap = localizations
            .GroupBy(localization => LanguageCodeUtility.Canonicalize(localization.Culture), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group =>
                {
                    if (group.Count() > 1)
                    {
                        throw new ArgumentException($"Duplicate OS recovery boot menu localization was provided for culture '{group.Key}'.");
                    }

                    return group.Single() with
                    {
                        Culture = group.Key
                    };
                },
                StringComparer.OrdinalIgnoreCase);

        foreach (string culture in supportedCultures)
        {
            if (!localizationMap.ContainsKey(culture))
            {
                throw new ArgumentException($"An OS recovery boot menu localization is required for every supported culture. Missing culture: '{culture}'.");
            }
        }

        XElement[] entries = supportedCultures
            .Select(culture => CreateBootMenuEntry(localizationMap[culture]))
            .ToArray();

        var document = new XDocument(
            new XDeclaration("1.0", "utf-8", null),
            new XElement("BootShell", entries));

        return document.ToString(SaveOptions.DisableFormatting);
    }

    private static XElement CreateBootMenuEntry(OsRecoveryBootMenuLocalization localization)
    {
        string name = localization.Name.Trim();
        string description = localization.Description.Trim();

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException($"OS recovery boot menu name is required for culture '{localization.Culture}'.");
        }

        if (string.IsNullOrWhiteSpace(description))
        {
            throw new ArgumentException($"OS recovery boot menu description is required for culture '{localization.Culture}'.");
        }

        if (name.Length > 30 || description.Length > 30)
        {
            throw new ArgumentException(
                $"OS recovery boot menu name and description must not exceed 30 characters for culture '{localization.Culture}'.");
        }

        return new XElement(
            "WinRETool",
            new XAttribute("locale", LanguageCodeUtility.Canonicalize(localization.Culture).ToLowerInvariant()),
            new XElement("Name", name),
            new XElement("Description", description));
    }

    private static string CreateWinReConfigurationXml()
    {
        var document = new XDocument(
            new XDeclaration("1.0", "utf-8", null),
            new XElement(
                "Recovery",
                new XElement(
                    "RecoveryTools",
                    new XElement("RelativeFilePath", LauncherFileName))));

        return document.ToString(SaveOptions.DisableFormatting);
    }

    private static long CalculateManagedPayloadSizeBytes(string mountedImagePath, WinPeArchitecture architecture)
    {
        string runtimeIdentifier = architecture.ToDotnetRuntimeIdentifier();
        string sevenZipRuntimeFolder = architecture.ToSevenZipRuntimeFolder();
        string[] managedPaths =
        [
            Path.Combine(mountedImagePath, "Sources", "Recovery", "Tools", LauncherFileName),
            Path.Combine(mountedImagePath, "Sources", "Recovery", "Tools", WinReConfigFileName),
            Path.Combine(mountedImagePath, "Windows", "System32", "curl.exe"),
            Path.Combine(mountedImagePath, "Foundry", "Config", "foundry.connect.config.json"),
            Path.Combine(mountedImagePath, "Foundry", "Config", "foundry.deploy.config.json"),
            Path.Combine(mountedImagePath, "Foundry", "Config", "iana-windows-timezones.json"),
            Path.Combine(mountedImagePath, "Foundry", "Runtime", "Foundry.Connect", runtimeIdentifier),
            Path.Combine(mountedImagePath, "Foundry", "Tools", "7zip", sevenZipRuntimeFolder),
            Path.Combine(mountedImagePath, "Foundry", "Tools", "7zip", "License.txt"),
            Path.Combine(mountedImagePath, "Foundry", "Tools", "7zip", "readme.txt")
        ];

        return managedPaths.Sum(CalculatePathSizeBytes);
    }

    private static long CalculatePathSizeBytes(string path)
    {
        if (File.Exists(path))
        {
            return new FileInfo(path).Length;
        }

        if (!Directory.Exists(path))
        {
            return 0;
        }

        return Directory
            .EnumerateFiles(path, "*", SearchOption.AllDirectories)
            .Sum(filePath => new FileInfo(filePath).Length);
    }

    private static void ProvisionBundledSevenZip(string mountedImagePath, OsRecoveryPayloadProvisioningOptions options)
    {
        string runtimeFolder = options.Architecture.ToSevenZipRuntimeFolder();
        string sourceRootPath = options.SevenZipSourceDirectoryPath;
        string sourceExecutablePath = Path.Combine(sourceRootPath, runtimeFolder, "7za.exe");
        string sourceLicensePath = Path.Combine(sourceRootPath, "License.txt");
        string sourceReadmePath = Path.Combine(sourceRootPath, "readme.txt");

        if (!File.Exists(sourceExecutablePath) || !File.Exists(sourceLicensePath) || !File.Exists(sourceReadmePath))
        {
            throw new IOException($"Bundled 7-Zip assets are incomplete under '{sourceRootPath}' for runtime '{runtimeFolder}'.");
        }

        string destinationToolsRootPath = Path.Combine(mountedImagePath, "Foundry", "Tools", "7zip");
        string destinationRuntimePath = Path.Combine(destinationToolsRootPath, runtimeFolder);
        Directory.CreateDirectory(destinationRuntimePath);

        File.Copy(sourceExecutablePath, Path.Combine(destinationRuntimePath, "7za.exe"), overwrite: true);
        File.Copy(sourceLicensePath, Path.Combine(destinationToolsRootPath, "License.txt"), overwrite: true);
        File.Copy(sourceReadmePath, Path.Combine(destinationToolsRootPath, "readme.txt"), overwrite: true);
    }

    private static string LoadLauncherContent()
    {
        Assembly assembly = typeof(OsRecoveryPayloadProvisioningService).Assembly;
        using Stream? stream = assembly.GetManifestResourceStream(LauncherResourceName);
        if (stream is null)
        {
            throw new InvalidOperationException($"Embedded recovery launcher resource '{LauncherResourceName}' was not found.");
        }

        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    private static WinPeDiagnostic? ValidateOptions(OsRecoveryPayloadProvisioningOptions? options)
    {
        if (options is null)
        {
            return new WinPeDiagnostic(
                WinPeErrorCodes.ValidationFailed,
                "OS recovery payload provisioning options are required.",
                "Provide a non-null OsRecoveryPayloadProvisioningOptions instance.");
        }

        if (string.IsNullOrWhiteSpace(options.MountedImagePath))
        {
            return new WinPeDiagnostic(
                WinPeErrorCodes.ValidationFailed,
                "Mounted image path is required for OS recovery payload provisioning.",
                "Set OsRecoveryPayloadProvisioningOptions.MountedImagePath.");
        }

        if (string.IsNullOrWhiteSpace(options.WorkingDirectoryPath))
        {
            return new WinPeDiagnostic(
                WinPeErrorCodes.ValidationFailed,
                "Working directory path is required for OS recovery payload provisioning.",
                "Set OsRecoveryPayloadProvisioningOptions.WorkingDirectoryPath.");
        }

        if (!Enum.IsDefined(options.Architecture))
        {
            return new WinPeDiagnostic(
                WinPeErrorCodes.ValidationFailed,
                "WinPE architecture value is invalid.",
                $"Value: '{options.Architecture}'.");
        }

        if (string.IsNullOrWhiteSpace(options.FoundryConnectConfigurationJson))
        {
            return new WinPeDiagnostic(
                WinPeErrorCodes.ValidationFailed,
                "Foundry.Connect configuration JSON is required for OS recovery payload provisioning.",
                "Set OsRecoveryPayloadProvisioningOptions.FoundryConnectConfigurationJson.");
        }

        if (string.IsNullOrWhiteSpace(options.DeployConfigurationJson))
        {
            return new WinPeDiagnostic(
                WinPeErrorCodes.ValidationFailed,
                "Foundry.Deploy configuration JSON is required for OS recovery payload provisioning.",
                "Set OsRecoveryPayloadProvisioningOptions.DeployConfigurationJson.");
        }

        if (string.IsNullOrWhiteSpace(options.IanaWindowsTimeZoneMapJson))
        {
            return new WinPeDiagnostic(
                WinPeErrorCodes.ValidationFailed,
                "IANA Windows time zone map JSON is required for OS recovery payload provisioning.",
                "Set OsRecoveryPayloadProvisioningOptions.IanaWindowsTimeZoneMapJson.");
        }

        if (string.IsNullOrWhiteSpace(options.SevenZipSourceDirectoryPath))
        {
            return new WinPeDiagnostic(
                WinPeErrorCodes.ValidationFailed,
                "Bundled 7-Zip source directory is required for OS recovery payload provisioning.",
                "Set OsRecoveryPayloadProvisioningOptions.SevenZipSourceDirectoryPath.");
        }

        if (string.IsNullOrWhiteSpace(options.CurlExecutableSourcePath) || !File.Exists(options.CurlExecutableSourcePath))
        {
            return new WinPeDiagnostic(
                WinPeErrorCodes.ValidationFailed,
                "curl.exe source path is required for OS recovery payload provisioning.",
                $"Expected file: '{options.CurlExecutableSourcePath}'.");
        }

        if (!options.Connect.IsEnabled)
        {
            return new WinPeDiagnostic(
                WinPeErrorCodes.ValidationFailed,
                "Foundry.Connect runtime payload provisioning must be enabled for OS recovery.",
                "Set OsRecoveryPayloadProvisioningOptions.Connect.IsEnabled to true.");
        }

        if (options.MaxManagedPayloadSizeBytes <= 0)
        {
            return new WinPeDiagnostic(
                WinPeErrorCodes.ValidationFailed,
                "Managed payload size budget must be greater than zero.",
                $"Configured budget: '{options.MaxManagedPayloadSizeBytes}'.");
        }

        return null;
    }
}
