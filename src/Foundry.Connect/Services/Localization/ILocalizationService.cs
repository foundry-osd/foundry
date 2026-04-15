using System.Globalization;

namespace Foundry.Connect.Services.Localization;

public interface ILocalizationService
{
    CultureInfo CurrentCulture { get; }
    StringsWrapper Strings { get; }
    event EventHandler? LanguageChanged;
    void SetCulture(CultureInfo culture);
}
