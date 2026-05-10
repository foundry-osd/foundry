using Foundry.Core.Services.WinPe;

namespace Foundry.Core.Services.Media;

public sealed record MediaPreflightEvaluation
{
    public bool CanGenerateIsoSummary { get; init; }
    public bool CanGenerateUsbSummary { get; init; }
    public bool CanCreateIso { get; init; }
    public bool CanCreateUsb { get; init; }
    public UsbPartitionStyle EffectiveUsbPartitionStyle { get; init; }
    public IReadOnlyList<MediaPreflightBlockingReason> IsoBlockingReasons { get; init; } = [];
    public IReadOnlyList<MediaPreflightBlockingReason> UsbBlockingReasons { get; init; } = [];
}
