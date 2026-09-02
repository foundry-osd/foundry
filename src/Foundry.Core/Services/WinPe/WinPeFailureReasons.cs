// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Services.WinPe;

public static class WinPeFailureReasons
{
    public const string InvalidInput = "invalid_input";
    public const string ToolNotFound = "tool_not_found";
    public const string ProcessStartFailed = "process_start_failed";
    public const string NonZeroExit = "nonzero_exit";
    public const string HttpStatus = "http_status";
    public const string Transport = "transport";
    public const string Timeout = "timeout";
    public const string AccessDenied = "access_denied";
    public const string DiskValidation = "disk_validation";
    public const string IsoCreation = "iso_creation";
    public const string ArtifactMissing = "artifact_missing";
    public const string IoError = "io_error";
    public const string Cancelled = "cancelled";
    public const string Unexpected = "unexpected";
}
