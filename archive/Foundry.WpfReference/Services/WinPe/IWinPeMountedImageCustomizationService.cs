namespace Foundry.Services.WinPe;

internal interface IWinPeMountedImageCustomizationService
{
    Task<WinPeResult> CustomizeAsync(
        WinPeMountedImageCustomizationRequest request,
        CancellationToken cancellationToken);
}
