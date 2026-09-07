// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using Foundry.Localization;
using Foundry.Utilities.Runtime;

namespace Foundry.Connect.Services.Runtime;

/// <summary>Provides an early package smoke check that never enters Connect startup.</summary>
internal static class PackageValidationCommand
{
    internal static bool IsRequested(string[] args) => PackageValidation.IsRequested(args);

    internal static int Run(string[] args) => Run(args, Validate, Console.Out);

    internal static int Run(string[] args, Action<Architecture> validate, TextWriter output)
        => PackageValidation.Run(args, FoundryConnectApplicationInfo.Version, RuntimeInformation.ProcessArchitecture,
            RuntimeInformation.OSArchitecture, validate, output);

    private static void Validate(Architecture architecture)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        PackageValidation.CheckStage(PackageValidationStage.Resources, () => PackageValidation.ValidateResources(typeof(PackageValidationCommand).Assembly,
            FoundrySupportedCultures.CreateCatalog().CreateOptions(CultureInfo.InvariantCulture, key => key).Select(option => option.Code)));
        PackageValidation.CheckStage(PackageValidationStage.NativeLibraries, () => PackageValidation.ValidateNativeLibraries(
            PackageValidation.ResolvePackageDirectory(Environment.ProcessPath, "Foundry.Connect.exe"),
            Environment.GetEnvironmentVariable("DOTNET_BUNDLE_EXTRACT_BASE_DIR"),
            AppContext.GetData("NATIVE_DLL_SEARCH_DIRECTORIES") as string, architecture));
    }
}
