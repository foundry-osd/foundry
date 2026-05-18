using System.Globalization;
using Foundry.Localization;

namespace Foundry.Deploy.Services.Localization;

public sealed class LocalizationService : ResourceManagerLocalizationService, ILocalizationService
{
    public LocalizationService()
        : base(LocalizationText.ResourceManager, CultureInfo.CurrentUICulture)
    {
    }
}
