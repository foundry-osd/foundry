namespace Foundry.Core.Services.WinPe;

public interface IWinPeMountedImageCustomizationService
{
    Task<WinPeResult> CustomizeAsync(
        WinPeMountedImageCustomizationOptions options,
        CancellationToken cancellationToken = default);
}
