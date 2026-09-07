// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Reflection.PortableExecutable;
using System.Resources;
using System.Runtime.InteropServices;

namespace Foundry.Utilities.Runtime;

/// <summary>Checks packaged runtime inputs without starting application services or creating views.</summary>
public static class PackageValidation
{
    private static readonly string[] NativeLibraries =
    [
        "vcruntime140_cor3.dll", "D3DCompiler_47_cor3.dll", "PresentationNative_cor3.dll",
        "wpfgfx_cor3.dll", "PenImc_cor3.dll"
    ];

    /// <summary>Recognizes validation even when other arguments are invalid, preventing startup fallthrough.</summary>
    public static bool IsRequested(string[] args) => args.Contains("--validate-package", StringComparer.OrdinalIgnoreCase);

    /// <summary>Runs a strictly parsed, architecture-bound package check and emits only a fixed result code.</summary>
    public static int Run(string[] args, string actualVersion, Architecture processArchitecture,
        Architecture osArchitecture, Action<Architecture> validate, TextWriter output)
    {
        if (args.Length != 5 || !IsRequested(args)) return Finish(output, 2, "invalid_arguments");
        Dictionary<string, string> options = new(StringComparer.Ordinal);
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--validate-package") continue;
            if (args[i] is not ("--expected-version" or "--expected-runtime") || i + 1 >= args.Length ||
                string.IsNullOrWhiteSpace(args[i + 1]) || !options.TryAdd(args[i], args[++i]))
                return Finish(output, 2, "invalid_arguments");
        }
        if (!options.TryGetValue("--expected-version", out string? version) ||
            !options.TryGetValue("--expected-runtime", out string? runtime)) return Finish(output, 2, "invalid_arguments");
        Architecture? expected = runtime switch { "win-x64" => Architecture.X64, "win-arm64" => Architecture.Arm64, _ => null };
        if (expected is null) return Finish(output, 2, "invalid_arguments");
        if (!string.Equals(version, actualVersion, StringComparison.Ordinal)) return Finish(output, 3, "version_mismatch");
        if (processArchitecture != expected || osArchitecture != expected) return Finish(output, 3, "runtime_mismatch");
        try
        {
            validate(expected.Value);
            return Finish(output, 0, "validated");
        }
        catch (PackageStageException ex)
        {
            return Finish(output, 3, ex.Code);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return Finish(output, 3, "package_validation_failed");
        }
    }

    /// <summary>Identifies the apphost directory even when all-content bundling redirects BaseDirectory to extraction.</summary>
    public static string ResolvePackageDirectory(string? processPath, string executableName)
    {
        if (string.IsNullOrWhiteSpace(processPath) || !Path.IsPathFullyQualified(processPath) ||
            !string.Equals(Path.GetFileName(processPath), executableName, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Package apphost identity unavailable.");
        RejectReparseChain(processPath);
        return Path.GetDirectoryName(Path.GetFullPath(processPath))!;
    }

    /// <summary>Reports fixed validation stages without exposing exception messages or local package paths.</summary>
    public static void CheckStage(PackageValidationStage stage, Action check)
    {
        string code = stage switch
        {
            PackageValidationStage.Resources => "package_resources_failed",
            PackageValidationStage.EmbeddedAssets => "package_embedded_assets_failed",
            PackageValidationStage.PackagedRecords => "package_records_failed",
            PackageValidationStage.NativeLibraries => "package_native_libraries_failed",
            _ => throw new ArgumentOutOfRangeException(nameof(stage))
        };
        try { check(); }
        catch (Exception ex) when (ex is not OutOfMemoryException) { throw new PackageStageException(code); }
    }

    /// <summary>Reads exact neutral/satellite dictionaries and compiled-resource bytes without deserializing BAML.</summary>
    public static void ValidateResources(Assembly assembly, IEnumerable<string> cultures)
    {
        string name = assembly.GetName().Name!;
        CheckDictionary(assembly, name + ".Strings.Resources.resources");
        foreach (string culture in cultures.Where(value => value != "en-US"))
        {
            Assembly satellite = assembly.GetSatelliteAssembly(CultureInfo.GetCultureInfo(culture));
            if (satellite.GetName().CultureName != culture) throw new InvalidDataException("Satellite culture mismatch.");
            CheckDictionary(satellite, name + ".Strings.Resources." + culture + ".resources");
        }
        using Stream stream = OpenResource(assembly, name + ".g.resources");
        using ResourceReader reader = new(stream);
        HashSet<string> keys = new(StringComparer.Ordinal);
        foreach (DictionaryEntry entry in reader)
        {
            if (entry.Value is Stream bytes && bytes.Length > 0) keys.Add((string)entry.Key);
        }
        foreach (string key in new[] { "app.baml", "mainwindow.baml", "assets/icons/app.ico", "assets/images/foundry-about-logo.png" })
            if (!keys.Contains(key)) throw new InvalidDataException("Required compiled resource missing.");
    }

    /// <summary>Opens a nonempty embedded resource; ownership of the returned stream belongs to the caller.</summary>
    public static Stream OpenResource(Assembly assembly, string name)
    {
        Stream stream = assembly.GetManifestResourceStream(name) ?? throw new InvalidDataException("Required embedded resource missing.");
        if (stream.Length > 0) return stream;
        stream.Dispose();
        throw new InvalidDataException("Required embedded resource empty.");
    }

    /// <summary>Loads only matching package or owned bundle-extraction DLLs; no exported native operation is invoked.</summary>
    public static void ValidateNativeLibraries(string packageRoot, string? extractionRoot, string? nativeSearchDirectories, Architecture architecture)
        => ValidateNativeLibraries(packageRoot, extractionRoot, nativeSearchDirectories, architecture, NativeLibrary.Load, NativeLibrary.Free);

    internal static void ValidateNativeLibraries(string packageRoot, string? extractionRoot, string? nativeSearchDirectories,
        Architecture architecture, Func<string, nint> load, Action<nint> free)
    {
        string root = Path.GetFullPath(packageRoot);
        string? extraction = string.IsNullOrWhiteSpace(extractionRoot) ? null : Path.GetFullPath(extractionRoot);
        string[] candidates = (nativeSearchDirectories ?? string.Empty).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Prepend(root).Where(Path.IsPathFullyQualified).Select(Path.GetFullPath).Select(Path.TrimEndingDirectorySeparator)
            .Where(path => SamePath(path, root) || (extraction is not null && IsUnder(path, extraction)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(path => NativeLibraries.All(name => File.Exists(Path.Combine(path, name)))).ToArray();
        if (candidates.Length != 1) throw new InvalidDataException("Native package root missing or ambiguous.");
        string directory = candidates[0];
        RejectReparseChain(directory);
        foreach (string name in NativeLibraries)
        {
            string path = Path.Combine(directory, name);
            RejectReparseChain(path);
            using FileStream stream = File.OpenRead(path);
            ValidateMachine(stream, architecture);
        }
        List<nint> handles = [];
        try
        {
            foreach (string name in NativeLibraries) handles.Add(load(Path.Combine(directory, name)));
        }
        finally
        {
            for (int i = handles.Count - 1; i >= 0; i--) free(handles[i]);
        }
    }

    /// <summary>Validates a PE machine from bytes without executing the image.</summary>
    public static void ValidateMachine(Stream stream, Architecture architecture)
    {
        using PEReader reader = new(stream, PEStreamOptions.LeaveOpen);
        Machine expected = architecture switch { Architecture.X64 => Machine.Amd64, Architecture.Arm64 => Machine.Arm64, _ => throw new InvalidDataException("Unsupported architecture.") };
        if (reader.PEHeaders.PEHeader is null || reader.PEHeaders.CoffHeader.Machine != expected)
            throw new InvalidDataException("PE architecture mismatch.");
    }

    /// <summary>Rejects path redirection while reading a supplied package.</summary>
    public static void RejectReparseChain(string path)
    {
        for (string? current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new IOException("Package path redirects.");
    }

    private static bool SamePath(string a, string b) => string.Equals(Path.TrimEndingDirectorySeparator(a), Path.TrimEndingDirectorySeparator(b), StringComparison.OrdinalIgnoreCase);
    private static bool IsUnder(string path, string root) => path.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    private static void CheckDictionary(Assembly assembly, string name)
    {
        using Stream stream = OpenResource(assembly, name);
        using ResourceReader reader = new(stream);
        foreach (DictionaryEntry entry in reader)
            if (entry.Key is "Common.Cancel" && entry.Value is string value && !string.IsNullOrWhiteSpace(value)) return;
        throw new InvalidDataException("Required localization value missing.");
    }
    private static int Finish(TextWriter output, int exitCode, string code)
    {
        try { output.WriteLine("{\"code\":\"" + code + "\",\"exitCode\":" + exitCode.ToString(CultureInfo.InvariantCulture) + "}"); }
        catch (IOException) { }
        return exitCode;
    }

    private sealed class PackageStageException(string code) : Exception
    {
        internal string Code { get; } = code;
    }
}

/// <summary>Fixed package-check boundaries suitable for non-sensitive CI diagnostics.</summary>
public enum PackageValidationStage
{
    Resources,
    EmbeddedAssets,
    PackagedRecords,
    NativeLibraries
}
