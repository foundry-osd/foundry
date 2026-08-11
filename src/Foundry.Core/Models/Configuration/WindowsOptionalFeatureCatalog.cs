// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Services.WinPe;

namespace Foundry.Core.Models.Configuration;

/// <summary>
/// Provides the curated Windows optional feature tree shown by Windows Features.
/// </summary>
public static class WindowsOptionalFeatureCatalog
{
    private const string Frameworks = "Customization.WindowsOptionalFeatures.Category.Frameworks";
    private const string DirectoryServices = "Customization.WindowsOptionalFeatures.Category.DirectoryServices";
    private const string Virtualization = "Customization.WindowsOptionalFeatures.Category.Virtualization";
    private const string Legacy = "Customization.WindowsOptionalFeatures.Category.Legacy";
    private const string Containers = "Customization.WindowsOptionalFeatures.Category.Containers";
    private const string Networking = "Customization.WindowsOptionalFeatures.Category.Networking";
    private const string Media = "Customization.WindowsOptionalFeatures.Category.Media";
    private const string Security = "Customization.WindowsOptionalFeatures.Category.Security";
    private const string Printing = "Customization.WindowsOptionalFeatures.Category.Printing";
    private const string Web = "Customization.WindowsOptionalFeatures.Category.Web";
    private const string Management = "Customization.WindowsOptionalFeatures.Category.Management";
    private const string Compatibility = "Customization.WindowsOptionalFeatures.Category.Compatibility";
    private const string FileServices = "Customization.WindowsOptionalFeatures.Category.FileServices";
    private const string Diagnostics = "Customization.WindowsOptionalFeatures.Category.Diagnostics";
    private const string DeviceLockdown = "Customization.WindowsOptionalFeatures.Category.DeviceLockdown";
    private const string FileSystems = "Customization.WindowsOptionalFeatures.Category.FileSystems";
    private const string SearchAndIndexing = "Customization.WindowsOptionalFeatures.Category.SearchAndIndexing";
    private const string Messaging = "Customization.WindowsOptionalFeatures.Category.Messaging";
    private const string AiComponents = "Customization.WindowsOptionalFeatures.Category.AiComponents";

    private static readonly string[] HomeEditionIds = ["Core", "CoreN", "CoreSingleLanguage", "CoreCountrySpecific"];
    private static readonly string[] ProEducationEnterpriseEditionIds = ["Professional", "ProfessionalN", "Education", "EducationN", "Enterprise", "EnterpriseN"];
    private static readonly string[] ProEnterpriseEditionIds = ["Professional", "ProfessionalN", "Enterprise", "EnterpriseN"];
    private static readonly string[] NonProEnterpriseEditionIds = ["Core", "CoreN", "CoreSingleLanguage", "CoreCountrySpecific", "Education", "EducationN"];
    private static readonly string[] DeviceLockdownEditionIds = ["Education", "EducationN", "Enterprise", "EnterpriseN"];
    private static readonly string[] NonDeviceLockdownEditionIds = ["Core", "CoreN", "CoreSingleLanguage", "CoreCountrySpecific", "Professional", "ProfessionalN"];

    private static readonly WindowsOptionalFeatureCatalogEntry[] CatalogEntries = BuildEntries();
    private static readonly IReadOnlyDictionary<string, WindowsOptionalFeatureCatalogEntry> EntriesById = CatalogEntries
        .ToDictionary(entry => entry.Id, StringComparer.OrdinalIgnoreCase);
    private static readonly IReadOnlyDictionary<string, WindowsOptionalFeatureCatalogEntry> EntriesByFeatureName = CatalogEntries
        .ToDictionary(entry => entry.FeatureName, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<WindowsOptionalFeatureCatalogEntry> Entries => CatalogEntries;

    public static WindowsOptionalFeatureCatalogEntry? Find(string? id)
    {
        return !string.IsNullOrWhiteSpace(id) && EntriesById.TryGetValue(id.Trim(), out WindowsOptionalFeatureCatalogEntry? entry)
            ? entry
            : null;
    }

    public static WindowsOptionalFeatureCatalogEntry? FindByFeatureName(string? featureName)
    {
        return !string.IsNullOrWhiteSpace(featureName) && EntriesByFeatureName.TryGetValue(featureName.Trim(), out WindowsOptionalFeatureCatalogEntry? entry)
            ? entry
            : null;
    }

    public static IReadOnlyList<WindowsOptionalFeatureCatalogEntry> GetChildren(string parentId)
    {
        return CatalogEntries
            .Where(entry => string.Equals(entry.ParentId, parentId, StringComparison.OrdinalIgnoreCase))
            .OrderBy(entry => entry.SortOrder)
            .ToArray();
    }

    public static IReadOnlyList<WindowsOptionalFeatureCatalogEntry> GetAncestors(string id)
    {
        List<WindowsOptionalFeatureCatalogEntry> ancestors = [];
        WindowsOptionalFeatureCatalogEntry? current = Find(id);
        while (current?.ParentId is not null && Find(current.ParentId) is { } parent)
        {
            ancestors.Add(parent);
            current = parent;
        }

        ancestors.Reverse();
        return ancestors;
    }

    public static int GetDepth(string id) => GetAncestors(id).Count;

    public static WindowsOptionalFeatureCatalogEntry? GetEffectiveEntry(string? id)
    {
        WindowsOptionalFeatureCatalogEntry? entry = Find(id);
        if (entry is null)
        {
            return null;
        }

        WindowsOptionalFeatureCatalogEntry[] hierarchy = [.. GetAncestors(entry.Id), entry];
        IReadOnlyList<string> supportedEditions = hierarchy.LastOrDefault(item => item.KnownSupportedEditionIds.Count > 0)?.KnownSupportedEditionIds ?? [];
        IReadOnlyList<string> unsupportedEditions = hierarchy.LastOrDefault(item => item.KnownUnsupportedEditionIds.Count > 0)?.KnownUnsupportedEditionIds ?? [];
        IReadOnlyList<WinPeArchitecture> architectures = hierarchy.LastOrDefault(item => item.SupportedArchitectures.Count > 0)?.SupportedArchitectures ?? [];

        return entry with
        {
            KnownSupportedEditionIds = supportedEditions,
            KnownUnsupportedEditionIds = unsupportedEditions,
            SupportedArchitectures = architectures,
            MinimumBuild = hierarchy.Where(item => item.MinimumBuild.HasValue).Select(item => item.MinimumBuild).Max(),
            MaximumBuildExclusive = hierarchy.Where(item => item.MaximumBuildExclusive.HasValue).Select(item => item.MaximumBuildExclusive).Min(),
            RequiresSetupMediaSxs = hierarchy.Any(item => item.RequiresSetupMediaSxs),
            WarningResourceKey = hierarchy.LastOrDefault(item => item.WarningResourceKey is not null)?.WarningResourceKey
        };
    }

    private static WindowsOptionalFeatureCatalogEntry[] BuildEntries()
    {
        List<WindowsOptionalFeatureCatalogEntry> entries =
        [
            SourceEntry("NetFx3", Frameworks),
            SourceEntry("WCF-HTTP-Activation", Frameworks, "NetFx3"),
            SourceEntry("WCF-NonHTTP-Activation", Frameworks, "NetFx3"),
            Entry("NetFx4-AdvSrvs", Frameworks),
            Entry("NetFx4Extended-ASPNET45", Frameworks, "NetFx4-AdvSrvs"),
            Entry("WCF-Services45", Frameworks, "NetFx4-AdvSrvs"),
            Entry("WCF-HTTP-Activation45", Frameworks, "WCF-Services45"),
            Entry("WCF-TCP-Activation45", Frameworks, "WCF-Services45"),
            Entry("WCF-Pipe-Activation45", Frameworks, "WCF-Services45"),
            Entry("WCF-MSMQ-Activation45", Frameworks, "WCF-Services45"),
            Entry("WCF-TCP-PortSharing45", Frameworks, "WCF-Services45"),
            Entry("DirectoryServices-ADAM-Client", DirectoryServices),
            Entry("HyperV-KernelInt-VirtualDevice", Virtualization),
            Entry(
                "Containers-DisposableClientVM",
                Virtualization,
                supportedEditionIds: ProEducationEnterpriseEditionIds,
                unsupportedEditionIds: HomeEditionIds,
                minimumBuild: 22621,
                warningResourceKey: "Customization.WindowsOptionalFeatures.Warning.HardwareVirtualization"),
            Entry("LegacyComponents", Legacy),
            Entry("DirectPlay", Legacy, "LegacyComponents"),
            Entry(
                "Containers",
                Containers,
                supportedEditionIds: ProEnterpriseEditionIds,
                unsupportedEditionIds: NonProEnterpriseEditionIds),
            Entry("DataCenterBridging", Networking),
            Entry("MediaPlayback", Media, warningResourceKey: "Customization.WindowsOptionalFeatures.Warning.MediaFeaturePack"),
            Entry("WindowsMediaPlayer", Media, "MediaPlayback"),
            Entry("HostGuardian", Security),
            Entry(
                "Microsoft-Hyper-V-All",
                Virtualization,
                supportedEditionIds: ProEducationEnterpriseEditionIds,
                unsupportedEditionIds: HomeEditionIds,
                warningResourceKey: "Customization.WindowsOptionalFeatures.Warning.HardwareVirtualization"),
            Entry("Microsoft-Hyper-V", Virtualization, "Microsoft-Hyper-V-All"),
            Entry("Microsoft-Hyper-V-Hypervisor", Virtualization, "Microsoft-Hyper-V"),
            Entry("Microsoft-Hyper-V-Services", Virtualization, "Microsoft-Hyper-V"),
            Entry("Microsoft-Hyper-V-Tools-All", Virtualization, "Microsoft-Hyper-V-All"),
            Entry("Microsoft-Hyper-V-Management-Clients", Virtualization, "Microsoft-Hyper-V-Tools-All"),
            Entry("Microsoft-Hyper-V-Management-PowerShell", Virtualization, "Microsoft-Hyper-V-Tools-All"),
            Entry("Printing-PrintToPDFServices-Features", Printing),
            Entry("IIS-HostableWebCore", Web),
            Entry("IIS-WebServerRole", Web),
            Entry("IIS-FTPServer", Web, "IIS-WebServerRole"),
            Entry("IIS-FTPSvc", Web, "IIS-FTPServer"),
            Entry("IIS-FTPExtensibility", Web, "IIS-FTPServer"),
            Entry("IIS-WebServerManagementTools", Web, "IIS-WebServerRole"),
            Entry("IIS-ManagementConsole", Web, "IIS-WebServerManagementTools"),
            Entry("IIS-ManagementScriptingTools", Web, "IIS-WebServerManagementTools"),
            Entry("IIS-ManagementService", Web, "IIS-WebServerManagementTools"),
            Entry("IIS-IIS6ManagementCompatibility", Web, "IIS-WebServerManagementTools"),
            Entry("IIS-Metabase", Web, "IIS-IIS6ManagementCompatibility"),
            Entry("IIS-WMICompatibility", Web, "IIS-IIS6ManagementCompatibility"),
            Entry("IIS-LegacyScripts", Web, "IIS-IIS6ManagementCompatibility"),
            Entry("IIS-LegacySnapIn", Web, "IIS-IIS6ManagementCompatibility"),
            Entry("IIS-WebServer", Web, "IIS-WebServerRole"),
            Entry("IIS-ApplicationDevelopment", Web, "IIS-WebServer"),
            SourceEntry("IIS-NetFxExtensibility", Web, "IIS-ApplicationDevelopment"),
            Entry("IIS-NetFxExtensibility45", Web, "IIS-ApplicationDevelopment"),
            Entry("IIS-ApplicationInit", Web, "IIS-ApplicationDevelopment"),
            Entry("IIS-ASP", Web, "IIS-ApplicationDevelopment"),
            SourceEntry("IIS-ASPNET", Web, "IIS-ApplicationDevelopment"),
            Entry("IIS-ASPNET45", Web, "IIS-ApplicationDevelopment"),
            Entry("IIS-CGI", Web, "IIS-ApplicationDevelopment"),
            Entry("IIS-ISAPIExtensions", Web, "IIS-ApplicationDevelopment"),
            Entry("IIS-ISAPIFilter", Web, "IIS-ApplicationDevelopment"),
            Entry("IIS-ServerSideIncludes", Web, "IIS-ApplicationDevelopment"),
            Entry("IIS-WebSockets", Web, "IIS-ApplicationDevelopment"),
            Entry("IIS-CommonHttpFeatures", Web, "IIS-WebServer"),
            Entry("IIS-DefaultDocument", Web, "IIS-CommonHttpFeatures"),
            Entry("IIS-DirectoryBrowsing", Web, "IIS-CommonHttpFeatures"),
            Entry("IIS-HttpErrors", Web, "IIS-CommonHttpFeatures"),
            Entry("IIS-HttpRedirect", Web, "IIS-CommonHttpFeatures"),
            Entry("IIS-StaticContent", Web, "IIS-CommonHttpFeatures"),
            Entry("IIS-WebDAV", Web, "IIS-CommonHttpFeatures"),
            Entry("IIS-HealthAndDiagnostics", Web, "IIS-WebServer"),
            Entry("IIS-CustomLogging", Web, "IIS-HealthAndDiagnostics"),
            Entry("IIS-HttpLogging", Web, "IIS-HealthAndDiagnostics"),
            Entry("IIS-LoggingLibraries", Web, "IIS-HealthAndDiagnostics"),
            Entry("IIS-ODBCLogging", Web, "IIS-HealthAndDiagnostics"),
            Entry("IIS-RequestMonitor", Web, "IIS-HealthAndDiagnostics"),
            Entry("IIS-HttpTracing", Web, "IIS-HealthAndDiagnostics"),
            Entry("IIS-Performance", Web, "IIS-WebServer"),
            Entry("IIS-HttpCompressionDynamic", Web, "IIS-Performance"),
            Entry("IIS-HttpCompressionStatic", Web, "IIS-Performance"),
            Entry("IIS-Security", Web, "IIS-WebServer"),
            Entry("IIS-BasicAuthentication", Web, "IIS-Security"),
            Entry("IIS-CertProvider", Web, "IIS-Security"),
            Entry("IIS-ClientCertificateMappingAuthentication", Web, "IIS-Security"),
            Entry("IIS-DigestAuthentication", Web, "IIS-Security"),
            Entry("IIS-IISCertificateMappingAuthentication", Web, "IIS-Security"),
            Entry("IIS-IPSecurity", Web, "IIS-Security"),
            Entry("IIS-RequestFiltering", Web, "IIS-Security"),
            Entry("IIS-URLAuthorization", Web, "IIS-Security"),
            Entry("IIS-WindowsAuthentication", Web, "IIS-Security"),
            Entry("WAS-WindowsActivationService", Web),
            Entry("WAS-ProcessModel", Web, "WAS-WindowsActivationService"),
            Entry("WAS-NetFxEnvironment", Web, "WAS-WindowsActivationService"),
            Entry("WAS-ConfigurationAPI", Web, "WAS-WindowsActivationService"),
            Entry("Printing-XPSServices-Features", Printing),
            Entry("MultiPoint-Connector", Management),
            Entry("MultiPoint-Connector-Services", Management, "MultiPoint-Connector"),
            Entry("MultiPoint-Tools", Management, "MultiPoint-Connector"),
            Entry("HyperV-Guest-KernelInt", Virtualization),
            Entry("HypervisorPlatform", Virtualization, warningResourceKey: "Customization.WindowsOptionalFeatures.Warning.HardwareVirtualization"),
            Entry("VirtualMachinePlatform", Virtualization, warningResourceKey: "Customization.WindowsOptionalFeatures.Warning.HardwareVirtualization"),
            Entry("MSRDC-Infrastructure", Compatibility),
            Entry("Containers-Server-For-Application-Guard", Containers),
            Entry("MSMQ-Container", Messaging),
            Entry("MSMQ-Server", Messaging, "MSMQ-Container"),
            Entry("MSMQ-ADIntegration", Messaging, "MSMQ-Server"),
            Entry("MSMQ-HTTP", Messaging, "MSMQ-Server"),
            Entry("MSMQ-Multicast", Messaging, "MSMQ-Server"),
            Entry("MSMQ-Triggers", Messaging, "MSMQ-Server"),
            Entry("MSMQ-DCOMProxy", Messaging, "MSMQ-Container"),
            Entry("Printing-Foundation-Features", Printing),
            Entry("Printing-Foundation-InternetPrinting-Client", Printing, "Printing-Foundation-Features"),
            Entry("Printing-Foundation-LPDPrintService", Printing, "Printing-Foundation-Features"),
            Entry("Printing-Foundation-LPRPortMonitor", Printing, "Printing-Foundation-Features"),
            Entry("ServicesForNFS-ClientOnly", FileServices),
            Entry("ClientForNFS-Infrastructure", FileServices, "ServicesForNFS-ClientOnly"),
            Entry("NFS-Administration", FileServices, "ServicesForNFS-ClientOnly"),
            Entry("SimpleTCP", Networking),
            Entry("SmbDirect", FileServices, warningResourceKey: "Customization.WindowsOptionalFeatures.Warning.Rdma"),
            Entry("Microsoft-Windows-Subsystem-Linux", Virtualization),
            Entry("SMB1Protocol", Legacy),
            Entry("SMB1Protocol-Client", Legacy, "SMB1Protocol"),
            Entry("SMB1Protocol-Server", Legacy, "SMB1Protocol"),
            Entry("SMB1Protocol-Deprecation", Legacy, "SMB1Protocol"),
            Entry("Sysmon", Diagnostics),
            Entry("TelnetClient", Legacy),
            Entry("TFTP", Legacy),
            Entry(
                "Client-DeviceLockdown",
                DeviceLockdown,
                supportedEditionIds: DeviceLockdownEditionIds,
                unsupportedEditionIds: NonDeviceLockdownEditionIds),
            Entry("Client-EmbeddedBootExp", DeviceLockdown, "Client-DeviceLockdown"),
            Entry("Client-EmbeddedLogon", DeviceLockdown, "Client-DeviceLockdown"),
            Entry("Client-EmbeddedShellLauncher", DeviceLockdown, "Client-DeviceLockdown"),
            Entry("Client-KeyboardFilter", DeviceLockdown, "Client-DeviceLockdown"),
            Entry("Client-UnifiedWriteFilter", DeviceLockdown, "Client-DeviceLockdown"),
            Entry("Windows-Identity-Foundation", Frameworks),
            Entry("Client-ProjFS", FileSystems),
            Entry("TIFFIFilter", SearchAndIndexing),
            Entry("WorkFolders-Client", FileServices),
            Entry("Recall", AiComponents, warningResourceKey: "Customization.WindowsOptionalFeatures.Warning.CopilotPlus")
        ];

        return entries.Select((entry, index) => entry with { SortOrder = index }).ToArray();
    }

    private static WindowsOptionalFeatureCatalogEntry SourceEntry(string featureName, string categoryResourceKey, string? parentFeatureName = null)
    {
        return Entry(
            featureName,
            categoryResourceKey,
            parentFeatureName,
            maximumBuildExclusive: 28000,
            requiresSetupMediaSxs: true);
    }

    private static WindowsOptionalFeatureCatalogEntry Entry(
        string featureName,
        string categoryResourceKey,
        string? parentFeatureName = null,
        IReadOnlyList<string>? supportedEditionIds = null,
        IReadOnlyList<string>? unsupportedEditionIds = null,
        int? minimumBuild = null,
        int? maximumBuildExclusive = null,
        bool requiresSetupMediaSxs = false,
        string? warningResourceKey = null)
    {
        return new WindowsOptionalFeatureCatalogEntry
        {
            Id = BuildId(featureName),
            FeatureName = featureName,
            DisplayNameResourceKey = $"Customization.WindowsOptionalFeatures.Feature.{featureName}",
            CategoryResourceKey = categoryResourceKey,
            ParentId = parentFeatureName is null ? null : BuildId(parentFeatureName),
            KnownSupportedEditionIds = supportedEditionIds ?? [],
            KnownUnsupportedEditionIds = unsupportedEditionIds ?? [],
            SupportedArchitectures = [WinPeArchitecture.X64, WinPeArchitecture.Arm64],
            MinimumBuild = minimumBuild,
            MaximumBuildExclusive = maximumBuildExclusive,
            RequiresSetupMediaSxs = requiresSetupMediaSxs,
            WarningResourceKey = warningResourceKey
        };
    }

    private static string BuildId(string featureName) => $"wf:{featureName.ToLowerInvariant()}";
}
