// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Foundry.Localization;
using Foundry.Utilities.Runtime;

namespace Foundry.Deploy.Services.Runtime;

/// <summary>Checks the delivered Deploy package without startup, offline-readiness or deployment services.</summary>
internal static class PackageValidationCommand
{
    private const string ServiceUiHash = "1BE85A64AAD2C3CAA0DC28705B49A1548E85157F4D2D522C20FEC4B4570A623F";
    private static readonly string[] Scripts =
    [
        "PreOobe.Cleanup-PreOobe.ps1", "PreOobe.Foundry-PreOobeFunctions.ps1", "PreOobe.Import-NetworkProfiles.ps1",
        "PreOobe.Install-DriverPack.ps1", "PreOobe.Invoke-FoundryPreOobe.ps1", "PreOobe.Remove-AiComponents.ps1",
        "PreOobe.Remove-AppX.ps1", "AutopilotRegistration.Foundry-AutopilotProtocol.ps1",
        "AutopilotRegistration.Start-FoundryAutopilotRegistration.ps1"
    ];

    internal static bool IsRequested(string[] args) => PackageValidation.IsRequested(args);
    internal static int Run(string[] args) => Run(args, Validate, Console.Out);
    internal static int Run(string[] args, Action<Architecture> validate, TextWriter output)
        => PackageValidation.Run(args, FoundryDeployApplicationInfo.Version, RuntimeInformation.ProcessArchitecture,
            RuntimeInformation.OSArchitecture, validate, output);

    private static void Validate(Architecture architecture)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        Assembly assembly = typeof(PackageValidationCommand).Assembly;
        PackageValidation.CheckStage(PackageValidationStage.Resources, () => PackageValidation.ValidateResources(assembly,
            FoundrySupportedCultures.CreateCatalog().CreateOptions(CultureInfo.InvariantCulture, key => key).Select(option => option.Code)));
        PackageValidation.CheckStage(PackageValidationStage.EmbeddedAssets, () => ValidateEmbeddedAssets(assembly));
        PackageValidation.CheckStage(PackageValidationStage.PackagedRecords, () =>
            ValidatePackagedRecords(PackageValidation.ResolvePackageDirectory(Environment.ProcessPath, "Foundry.Deploy.exe")));
        PackageValidation.CheckStage(PackageValidationStage.NativeLibraries, () => PackageValidation.ValidateNativeLibraries(
            PackageValidation.ResolvePackageDirectory(Environment.ProcessPath, "Foundry.Deploy.exe"),
            Environment.GetEnvironmentVariable("DOTNET_BUNDLE_EXTRACT_BASE_DIR"),
            AppContext.GetData("NATIVE_DLL_SEARCH_DIRECTORIES") as string, architecture));
    }

    internal static void ValidateEmbeddedAssets(Assembly assembly)
    {
        foreach (string script in Scripts)
        {
            using Stream resource = PackageValidation.OpenResource(assembly, "Foundry.Deploy." + script);
        }
        using Stream serviceUi = PackageValidation.OpenResource(assembly, "Foundry.Deploy.AutopilotRegistration.ServiceUI.exe");
        ValidateServiceUi(serviceUi);
    }

    internal static void ValidateServiceUi(Stream stream)
    {
        if (stream.Length != 74008 || !Convert.ToHexString(SHA256.HashData(stream)).Equals(ServiceUiHash, StringComparison.Ordinal))
            throw new InvalidDataException("Embedded ServiceUI identity mismatch.");
        stream.Position = 0;
        PackageValidation.ValidateMachine(stream, Architecture.X64);
    }

    internal static void ValidatePackagedRecords(string packageRoot)
    {
        byte[] notices = ReadPackagedFile(packageRoot, "THIRD_PARTY_NOTICES.md", 1024 * 1024);
        if (string.IsNullOrWhiteSpace(Encoding.UTF8.GetString(notices))) throw new InvalidDataException("Notices empty.");
        using JsonDocument document = JsonDocument.Parse(ReadPackagedFile(packageRoot, "ServiceUI.provenance.json", 64 * 1024));
        JsonElement root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Provenance malformed.");
        HashSet<string> names = new(StringComparer.Ordinal);
        foreach (JsonProperty property in root.EnumerateObject())
            if (!names.Add(property.Name)) throw new InvalidDataException("Provenance duplicates a field.");
        if (root.GetProperty("schemaVersion").GetInt32() != 1 || root.GetProperty("size").GetInt64() != 74008 ||
            root.GetProperty("file").GetString() != "ServiceUI.exe" || root.GetProperty("fileVersion").GetString() != "1.3.0.0" ||
            root.GetProperty("sha256").GetString() != ServiceUiHash || root.GetProperty("peMachine").GetString() != "8664" ||
            root.GetProperty("architecture").GetString() != "x64" || root.GetProperty("expectedSigner").GetString() != "Microsoft Corporation" ||
            root.GetProperty("sourcePackageVerification").GetString() != "unverified" || root.GetProperty("redistributionVerification").GetString() != "unverified")
            throw new InvalidDataException("Provenance identity mismatch.");
    }

    private static byte[] ReadPackagedFile(string root, string name, int limit)
    {
        string path = Path.Combine(root, name);
        PackageValidation.RejectReparseChain(path);
        using FileStream stream = File.OpenRead(path);
        if (stream.Length is <= 0 || stream.Length > limit) throw new InvalidDataException("Package record length invalid.");
        byte[] bytes = new byte[checked((int)stream.Length)];
        stream.ReadExactly(bytes);
        return bytes;
    }
}
