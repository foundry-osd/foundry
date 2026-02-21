namespace Foundry.Services.WinPe;

public static class WinPeErrorCodes
{
    public const string ValidationFailed = "WINPE_VALIDATION_FAILED";
    public const string OperationBusy = "WINPE_OPERATION_BUSY";
    public const string ToolNotFound = "WINPE_TOOL_NOT_FOUND";
    public const string BuildFailed = "WINPE_BUILD_FAILED";
    public const string WimMountFailed = "WINPE_WIM_MOUNT_FAILED";
    public const string WimUnmountFailed = "WINPE_WIM_UNMOUNT_FAILED";
    public const string DriverCatalogFetchFailed = "WINPE_DRIVER_CATALOG_FETCH_FAILED";
    public const string DriverCatalogParseFailed = "WINPE_DRIVER_CATALOG_PARSE_FAILED";
    public const string DriverInjectionFailed = "WINPE_DRIVER_INJECTION_FAILED";
    public const string DriverExtractionFailed = "WINPE_DRIVER_EXTRACTION_FAILED";
    public const string DownloadFailed = "WINPE_DOWNLOAD_FAILED";
    public const string HashMismatch = "WINPE_HASH_MISMATCH";
    public const string IsoCreateFailed = "WINPE_ISO_CREATE_FAILED";
    public const string BootExUnsupported = "WINPE_BOOTEX_UNSUPPORTED";
    public const string PcaRemediationFailed = "WINPE_PCA_REMEDIATION_FAILED";
    public const string UsbQueryFailed = "WINPE_USB_QUERY_FAILED";
    public const string UsbUnsafeTarget = "WINPE_USB_UNSAFE_TARGET";
    public const string UsbIdentityMismatch = "WINPE_USB_IDENTITY_MISMATCH";
    public const string UsbProvisioningFailed = "WINPE_USB_PROVISIONING_FAILED";
    public const string UsbCopyFailed = "WINPE_USB_COPY_FAILED";
    public const string UsbVerificationFailed = "WINPE_USB_VERIFICATION_FAILED";
    public const string InternalError = "WINPE_INTERNAL_ERROR";
}
