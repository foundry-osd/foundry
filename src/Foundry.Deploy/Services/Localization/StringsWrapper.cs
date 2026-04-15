using System.ComponentModel;
using System.Globalization;
using System.Resources;

namespace Foundry.Deploy.Services.Localization;

public sealed class StringsWrapper : INotifyPropertyChanged
{
    private readonly ResourceManager _resourceManager;
    private CultureInfo _currentCulture;

    public StringsWrapper(ResourceManager resourceManager, CultureInfo currentCulture)
    {
        _resourceManager = resourceManager;
        _currentCulture = currentCulture;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string this[string key] => _resourceManager.GetString(key, _currentCulture) ?? key;

    public void SetCulture(CultureInfo culture)
    {
        if (_currentCulture != culture)
        {
            _currentCulture = culture;
            OnPropertyChanged("Item[]");
        }
    }

    private void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
