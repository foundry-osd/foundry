namespace Foundry.Core.Services.WinPe;

public interface IWinPeLanguageDiscoveryService
{
    WinPeResult<IReadOnlyList<string>> GetAvailableLanguages(WinPeLanguageDiscoveryOptions options);
}
