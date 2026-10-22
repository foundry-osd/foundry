// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Reflection;

namespace Foundry.Core.Services.Telemetry;

/// <summary>
/// Defines stable boot media creation step names used by telemetry.
/// </summary>
public static class MediaCreationStepNames
{
    public const string Unknown = "unknown";
    public const string ValidateUsbTarget = "Validate USB target";
    public const string ResolveWinPeTools = "Resolve WinPE tools";
    public const string PrepareRuntimePayloads = "Prepare runtime payloads";
    public const string CleanStaleWorkspaces = "Clean stale WinPE workspaces";
    public const string BuildWinPeWorkspace = "Build WinPE workspace";
    public const string GenerateProvisioningPayloads = "Generate provisioning payloads";
    public const string PrepareWinPeWorkspace = "Prepare WinPE workspace";
    public const string ResolveWinPeDrivers = "Resolve WinPE drivers";
    public const string CustomizeBootImage = "Customize boot image";
    public const string EvaluateSignaturePolicy = "Evaluate signature policy";
    public const string ResolveWinReSourceCatalog = "Resolve WinRE source catalog";
    public const string SelectWinReSourcePackage = "Select WinRE source package";
    public const string PrepareWinReSourcePackage = "Prepare WinRE source package";
    public const string DownloadWinReSourcePackage = "Download WinRE source package";
    public const string ResolveWinReImageIndex = "Resolve WinRE image index";
    public const string ExportWinReSourceImage = "Export WinRE source image";
    public const string MountWinReSourceImage = "Mount WinRE source image";
    public const string StageWinReDependencies = "Stage WinRE dependencies";
    public const string ReplaceBootImageWithWinRe = "Replace boot image with WinRE";
    public const string MountBootImage = "Mount boot image";
    public const string DownloadWinPeDriverPackage = "Download WinPE driver package";
    public const string InjectDriversIntoBootImage = "Inject drivers into boot image";
    public const string ApplyLanguageAndOptionalComponents = "Apply language and optional components";
    public const string ProvisionBootAssets = "Provision boot assets";
    public const string ProvisionRuntimePayloads = "Provision runtime payloads";
    public const string DownloadConnectRuntimePayload = "Download Connect runtime payload";
    public const string DownloadDeployRuntimePayload = "Download Deploy runtime payload";
    public const string CommitBootImageChanges = "Commit boot image changes";
    public const string DownloadMediaDependency = "Download media dependency";
    public const string PrepareIsoOutputPath = "Prepare ISO output path";
    public const string PrepareIsoWorkspace = "Prepare ISO workspace";
    public const string RunMakeWinPeMediaForIso = "Run MakeWinPEMedia for ISO";
    public const string FinalizeIsoOutput = "Finalize ISO output";
    public const string CheckUsbTargetSafety = "Check USB target safety";
    public const string PartitionAndFormatUsbTarget = "Partition and format USB target";
    public const string CopyWinPeMediaToUsb = "Copy WinPE media to USB";
    public const string VerifyUsbBootMedia = "Verify USB boot media";
    public const string PrepareUsbCachePartition = "Prepare USB cache partition";
    public const string ProvisionUsbRuntimePayloads = "Provision USB runtime payloads";
    public const string CreateFinalMedia = "Create final media";
    public const string CreateIsoMedia = "Create ISO media";
    public const string CreateUsbMedia = "Create USB media";
    public const string UpdateUsbMedia = "Update USB media";

    private static readonly HashSet<string> KnownStepNames = typeof(MediaCreationStepNames)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(field => field.IsLiteral && field.FieldType == typeof(string))
        .Select(field => (string)field.GetRawConstantValue()!)
        .ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// Returns <paramref name="stepName"/> when it is one of the stable step names, otherwise <see cref="Unknown"/>.
    /// </summary>
    /// <param name="stepName">Tracked or diagnostic step name.</param>
    /// <returns>A stable step name that can be sent as telemetry.</returns>
    /// <remarks>Diagnostic stages can embed configuration values, so only the fixed names may leave the process.</remarks>
    public static string Normalize(string? stepName) =>
        stepName is not null && KnownStepNames.Contains(stepName) ? stepName : Unknown;
}
