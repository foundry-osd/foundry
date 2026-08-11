// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;

namespace Foundry.Core.Tests.Configuration;

public sealed class WindowsOptionalFeatureCatalogTests
{
    [Fact]
    public void Entries_ContainTheCompleteVisibleFeatureSuperset()
    {
        Assert.Equal(ExpectedFeatureNames, WindowsOptionalFeatureCatalog.Entries.Select(entry => entry.FeatureName));
    }

    [Fact]
    public void Entries_HaveUniqueStableIdsAndFeatureNames()
    {
        Assert.Equal(
            WindowsOptionalFeatureCatalog.Entries.Count,
            WindowsOptionalFeatureCatalog.Entries.Select(entry => entry.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(
            WindowsOptionalFeatureCatalog.Entries.Count,
            WindowsOptionalFeatureCatalog.Entries.Select(entry => entry.FeatureName).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(
            WindowsOptionalFeatureCatalog.Entries,
            entry => Assert.Equal($"wf:{entry.FeatureName.ToLowerInvariant()}", entry.Id));
        Assert.Equal(
            WindowsOptionalFeatureCatalog.Entries.Count,
            WindowsOptionalFeatureCatalog.Entries.Select(entry => entry.DisplayNameResourceKey).Distinct(StringComparer.Ordinal).Count());
        Assert.All(
            WindowsOptionalFeatureCatalog.Entries,
            entry => Assert.DoesNotContain('-', entry.DisplayNameResourceKey));
    }

    [Fact]
    public void Entries_HaveValidParentsAndNoCycles()
    {
        IReadOnlyDictionary<string, WindowsOptionalFeatureCatalogEntry> byId = WindowsOptionalFeatureCatalog.Entries
            .ToDictionary(entry => entry.Id, StringComparer.OrdinalIgnoreCase);

        foreach (WindowsOptionalFeatureCatalogEntry entry in WindowsOptionalFeatureCatalog.Entries)
        {
            HashSet<string> ancestors = new(StringComparer.OrdinalIgnoreCase);
            WindowsOptionalFeatureCatalogEntry current = entry;
            while (current.ParentId is not null)
            {
                Assert.True(byId.TryGetValue(current.ParentId, out WindowsOptionalFeatureCatalogEntry? parent));
                Assert.True(ancestors.Add(parent.Id));
                current = parent;
            }
        }
    }

    [Fact]
    public void Entries_HaveValidCompatibilityMetadata()
    {
        HashSet<string> editionIds = WindowsEditionCatalog.SupportedDefinitions
            .Select(definition => definition.EditionId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (WindowsOptionalFeatureCatalogEntry entry in WindowsOptionalFeatureCatalog.Entries)
        {
            Assert.False(string.IsNullOrWhiteSpace(entry.Id));
            Assert.False(string.IsNullOrWhiteSpace(entry.FeatureName));
            Assert.False(string.IsNullOrWhiteSpace(entry.DisplayNameResourceKey));
            Assert.False(string.IsNullOrWhiteSpace(entry.CategoryResourceKey));
            Assert.NotEmpty(entry.SupportedArchitectures);
            Assert.All(entry.SupportedArchitectures, architecture => Assert.True(Enum.IsDefined(architecture)));
            Assert.True(entry.MinimumBuild is null or > 0);
            Assert.True(entry.MaximumBuildExclusive is null or > 0);
            Assert.True(entry.MinimumBuild is null || entry.MaximumBuildExclusive is null || entry.MinimumBuild < entry.MaximumBuildExclusive);
            Assert.All(entry.KnownSupportedEditionIds, editionId => Assert.Contains(editionId, editionIds));
            Assert.All(entry.KnownUnsupportedEditionIds, editionId => Assert.Contains(editionId, editionIds));
            Assert.Empty(entry.KnownSupportedEditionIds.Intersect(entry.KnownUnsupportedEditionIds, StringComparer.OrdinalIgnoreCase));

            if (entry.ParentId is not null)
            {
                WindowsOptionalFeatureCatalogEntry parent = Assert.IsType<WindowsOptionalFeatureCatalogEntry>(
                    WindowsOptionalFeatureCatalog.Find(entry.ParentId));
                Assert.Equal(parent.CategoryResourceKey, entry.CategoryResourceKey);
                Assert.True(parent.SortOrder < entry.SortOrder);
            }
        }

        Assert.Equal(
            WindowsOptionalFeatureCatalog.Entries.Count,
            WindowsOptionalFeatureCatalog.Entries.Select(entry => entry.SortOrder).Distinct().Count());
    }

    [Theory]
    [InlineData("Internet-Explorer-Optional-amd64")]
    [InlineData("MicrosoftWindowsPowerShellV2Root")]
    [InlineData("MicrosoftWindowsPowerShellV2")]
    [InlineData("Microsoft-RemoteDesktopConnection")]
    [InlineData("SearchEngine-Client-Package")]
    [InlineData("AppServerClient")]
    [InlineData("Containers-HNS")]
    [InlineData("Containers-SDN")]
    public void Entries_HiddenServicingFeature_IsExcluded(string featureName)
    {
        Assert.DoesNotContain(
            WindowsOptionalFeatureCatalog.Entries,
            entry => string.Equals(entry.FeatureName, featureName, StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("NetFx3")]
    [InlineData("WCF-HTTP-Activation")]
    [InlineData("WCF-NonHTTP-Activation")]
    [InlineData("IIS-ASPNET")]
    [InlineData("IIS-NetFxExtensibility")]
    public void Entries_NetFx3DependentEntry_RequiresMatchingSource(string featureName)
    {
        WindowsOptionalFeatureCatalogEntry entry = FindFeature(featureName);

        Assert.True(entry.RequiresSetupMediaSxs);
        Assert.Equal(28000, entry.MaximumBuildExclusive);
    }

    [Fact]
    public void Entries_RequireMatchingSourceOnlyForNetFx3Payloads()
    {
        Assert.Equal(
            ["NetFx3", "WCF-HTTP-Activation", "WCF-NonHTTP-Activation", "IIS-NetFxExtensibility", "IIS-ASPNET"],
            WindowsOptionalFeatureCatalog.Entries
                .Where(entry => entry.RequiresSetupMediaSxs)
                .Select(entry => entry.FeatureName));
    }

    [Fact]
    public void Find_UsesCaseInsensitiveCanonicalLookups()
    {
        WindowsOptionalFeatureCatalogEntry entry = Assert.IsType<WindowsOptionalFeatureCatalogEntry>(
            WindowsOptionalFeatureCatalog.Find(" WF:NETFX3 "));

        Assert.Equal("NetFx3", entry.FeatureName);
    }

    [Fact]
    public void Entries_WindowsProcessActivationService_ContainsVisibleChildren()
    {
        WindowsOptionalFeatureCatalogEntry parent = FindFeature("WAS-WindowsActivationService");

        Assert.Equal(
            ["WAS-ProcessModel", "WAS-NetFxEnvironment", "WAS-ConfigurationAPI"],
            WindowsOptionalFeatureCatalog.Entries
                .Where(entry => string.Equals(entry.ParentId, parent.Id, StringComparison.OrdinalIgnoreCase))
                .OrderBy(entry => entry.SortOrder)
                .Select(entry => entry.FeatureName));
    }

    private static WindowsOptionalFeatureCatalogEntry FindFeature(string featureName)
        => WindowsOptionalFeatureCatalog.Entries.Single(
            entry => string.Equals(entry.FeatureName, featureName, StringComparison.OrdinalIgnoreCase));

    private static readonly string[] ExpectedFeatureNames =
    [
        "NetFx3",
        "WCF-HTTP-Activation",
        "WCF-NonHTTP-Activation",
        "NetFx4-AdvSrvs",
        "NetFx4Extended-ASPNET45",
        "WCF-Services45",
        "WCF-HTTP-Activation45",
        "WCF-TCP-Activation45",
        "WCF-Pipe-Activation45",
        "WCF-MSMQ-Activation45",
        "WCF-TCP-PortSharing45",
        "DirectoryServices-ADAM-Client",
        "HyperV-KernelInt-VirtualDevice",
        "Containers-DisposableClientVM",
        "LegacyComponents",
        "DirectPlay",
        "Containers",
        "DataCenterBridging",
        "MediaPlayback",
        "WindowsMediaPlayer",
        "HostGuardian",
        "Microsoft-Hyper-V-All",
        "Microsoft-Hyper-V",
        "Microsoft-Hyper-V-Hypervisor",
        "Microsoft-Hyper-V-Services",
        "Microsoft-Hyper-V-Tools-All",
        "Microsoft-Hyper-V-Management-Clients",
        "Microsoft-Hyper-V-Management-PowerShell",
        "Printing-PrintToPDFServices-Features",
        "IIS-HostableWebCore",
        "IIS-WebServerRole",
        "IIS-FTPServer",
        "IIS-FTPSvc",
        "IIS-FTPExtensibility",
        "IIS-WebServerManagementTools",
        "IIS-ManagementConsole",
        "IIS-ManagementScriptingTools",
        "IIS-ManagementService",
        "IIS-IIS6ManagementCompatibility",
        "IIS-Metabase",
        "IIS-WMICompatibility",
        "IIS-LegacyScripts",
        "IIS-LegacySnapIn",
        "IIS-WebServer",
        "IIS-ApplicationDevelopment",
        "IIS-NetFxExtensibility",
        "IIS-NetFxExtensibility45",
        "IIS-ApplicationInit",
        "IIS-ASP",
        "IIS-ASPNET",
        "IIS-ASPNET45",
        "IIS-CGI",
        "IIS-ISAPIExtensions",
        "IIS-ISAPIFilter",
        "IIS-ServerSideIncludes",
        "IIS-WebSockets",
        "IIS-CommonHttpFeatures",
        "IIS-DefaultDocument",
        "IIS-DirectoryBrowsing",
        "IIS-HttpErrors",
        "IIS-HttpRedirect",
        "IIS-StaticContent",
        "IIS-WebDAV",
        "IIS-HealthAndDiagnostics",
        "IIS-CustomLogging",
        "IIS-HttpLogging",
        "IIS-LoggingLibraries",
        "IIS-ODBCLogging",
        "IIS-RequestMonitor",
        "IIS-HttpTracing",
        "IIS-Performance",
        "IIS-HttpCompressionDynamic",
        "IIS-HttpCompressionStatic",
        "IIS-Security",
        "IIS-BasicAuthentication",
        "IIS-CertProvider",
        "IIS-ClientCertificateMappingAuthentication",
        "IIS-DigestAuthentication",
        "IIS-IISCertificateMappingAuthentication",
        "IIS-IPSecurity",
        "IIS-RequestFiltering",
        "IIS-URLAuthorization",
        "IIS-WindowsAuthentication",
        "WAS-WindowsActivationService",
        "WAS-ProcessModel",
        "WAS-NetFxEnvironment",
        "WAS-ConfigurationAPI",
        "Printing-XPSServices-Features",
        "MultiPoint-Connector",
        "MultiPoint-Connector-Services",
        "MultiPoint-Tools",
        "HyperV-Guest-KernelInt",
        "HypervisorPlatform",
        "VirtualMachinePlatform",
        "MSRDC-Infrastructure",
        "Containers-Server-For-Application-Guard",
        "MSMQ-Container",
        "MSMQ-Server",
        "MSMQ-ADIntegration",
        "MSMQ-HTTP",
        "MSMQ-Multicast",
        "MSMQ-Triggers",
        "MSMQ-DCOMProxy",
        "Printing-Foundation-Features",
        "Printing-Foundation-InternetPrinting-Client",
        "Printing-Foundation-LPDPrintService",
        "Printing-Foundation-LPRPortMonitor",
        "ServicesForNFS-ClientOnly",
        "ClientForNFS-Infrastructure",
        "NFS-Administration",
        "SimpleTCP",
        "SmbDirect",
        "Microsoft-Windows-Subsystem-Linux",
        "SMB1Protocol",
        "SMB1Protocol-Client",
        "SMB1Protocol-Server",
        "SMB1Protocol-Deprecation",
        "Sysmon",
        "TelnetClient",
        "TFTP",
        "Client-DeviceLockdown",
        "Client-EmbeddedBootExp",
        "Client-EmbeddedLogon",
        "Client-EmbeddedShellLauncher",
        "Client-KeyboardFilter",
        "Client-UnifiedWriteFilter",
        "Windows-Identity-Foundation",
        "Client-ProjFS",
        "TIFFIFilter",
        "WorkFolders-Client",
        "Recall"
    ];
}
