using Foundry.Core.Services.WinPe;

namespace Foundry.Core.Services.Media;

/// <summary>
/// Describes the media actions currently available and the reasons blocked actions cannot run.
/// </summary>
public sealed record MediaPreflightEvaluation
{
    /// <summary>
    /// Gets a value indicating whether ISO readiness can be summarized for display.
    /// </summary>
    public bool CanGenerateIsoSummary { get; init; }

    /// <summary>
    /// Gets a value indicating whether USB readiness can be summarized for display.
    /// </summary>
    public bool CanGenerateUsbSummary { get; init; }

    /// <summary>
    /// Gets a value indicating whether ISO creation can run now.
    /// </summary>
    public bool CanCreateIso { get; init; }

    /// <summary>
    /// Gets a value indicating whether USB creation can run now.
    /// </summary>
    public bool CanCreateUsb { get; init; }

    /// <summary>
    /// Gets the USB partition style that should be used after applying preflight rules.
    /// </summary>
    public UsbPartitionStyle EffectiveUsbPartitionStyle { get; init; }

    /// <summary>
    /// Gets reasons that currently block ISO creation.
    /// </summary>
    public IReadOnlyList<MediaPreflightBlockingReason> IsoBlockingReasons { get; init; } = [];

    /// <summary>
    /// Gets reasons that currently block USB creation.
    /// </summary>
    public IReadOnlyList<MediaPreflightBlockingReason> UsbBlockingReasons { get; init; } = [];
}
