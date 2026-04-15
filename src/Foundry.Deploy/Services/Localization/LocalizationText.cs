using System.Globalization;
using System.Resources;

namespace Foundry.Deploy.Services.Localization;

public static class LocalizationText
{
    public static readonly ResourceManager ResourceManager =
        new("Foundry.Deploy.Resources.AppStrings", typeof(LocalizationText).Assembly);

    public static string GetString(string key)
    {
        return ResourceManager.GetString(key, CultureInfo.CurrentUICulture) ?? key;
    }

    public static string Format(string key, params object[] args)
    {
        return string.Format(CultureInfo.CurrentUICulture, GetString(key), args);
    }
}
