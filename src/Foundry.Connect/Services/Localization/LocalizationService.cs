using System.ComponentModel;
using System.Globalization;
using System.Resources;

namespace Foundry.Connect.Services.Localization;

public sealed class LocalizationService : ILocalizationService, INotifyPropertyChanged
{
    private readonly ResourceManager _resourceManager;
    private readonly StringsWrapper _stringsWrapper;
    private CultureInfo _currentCulture;

    public LocalizationService()
    {
        _resourceManager = new ResourceManager("Foundry.Connect.Resources.AppStrings", typeof(LocalizationService).Assembly);
        _currentCulture = CultureInfo.CurrentUICulture;
        _stringsWrapper = new StringsWrapper(_resourceManager, _currentCulture);
    }

    public CultureInfo CurrentCulture
    {
        get => _currentCulture;
        private set
        {
            if (_currentCulture != value)
            {
                _currentCulture = value;
                CultureInfo.CurrentUICulture = _currentCulture;
                _stringsWrapper.SetCulture(_currentCulture);
                OnPropertyChanged(nameof(CurrentCulture));
            }
        }
    }

    public StringsWrapper Strings => _stringsWrapper;

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? LanguageChanged;

    public void SetCulture(CultureInfo culture)
    {
        CurrentCulture = culture;
        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
