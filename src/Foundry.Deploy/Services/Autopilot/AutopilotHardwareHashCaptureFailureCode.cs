// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Deploy.Services.Autopilot;

/// <summary>
/// Describes the capture-stage failure that occurred before Microsoft Graph upload can run.
/// </summary>
public enum AutopilotHardwareHashCaptureFailureCode
{
    /// <summary>
    /// The capture operation completed successfully.
    /// </summary>
    None,

    /// <summary>
    /// OA3Tool was not staged into the boot media runtime tools folder.
    /// </summary>
    ToolMissing,

    /// <summary>
    /// OA3Tool exited with a non-zero code.
    /// </summary>
    ToolFailed,

    /// <summary>
    /// The applied Windows image does not contain the required PCPKsp support library.
    /// </summary>
    SupportLibraryMissing,

    /// <summary>
    /// The PCPKsp support library could not be copied into the active WinPE System32 folder.
    /// </summary>
    SupportLibraryCopyFailed,

    /// <summary>
    /// OA3Tool could not load the copied PCPKsp support library.
    /// </summary>
    SupportLibraryLoadFailed,

    /// <summary>
    /// OA3Tool did not create the expected OA3 report XML.
    /// </summary>
    ReportMissing,

    /// <summary>
    /// The OA3 report XML could not be parsed.
    /// </summary>
    ReportInvalid,

    /// <summary>
    /// The OA3 report XML did not contain a hardware hash.
    /// </summary>
    HashMissing,

    /// <summary>
    /// The OA3 report XML did not contain a device serial number.
    /// </summary>
    SerialMissing
}
