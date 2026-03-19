using CommunityToolkit.Mvvm.ComponentModel;
using Foundry.Services.Localization;

namespace Foundry.ViewModels;

public abstract class LocalizedViewModelBase : ObservableObject, IDisposable
{
    private readonly ILocalizationService _localizationService;
    private bool _isDisposed;

    protected LocalizedViewModelBase(ILocalizationService localizationService)
    {
        _localizationService = localizationService ?? throw new ArgumentNullException(nameof(localizationService));
        _localizationService.LanguageChanged += OnLanguageChanged;
    }

    public StringsWrapper Strings => _localizationService.Strings;

    protected ILocalizationService LocalizationService => _localizationService;

    public virtual void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _localizationService.LanguageChanged -= OnLanguageChanged;
        _isDisposed = true;
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(Strings));
    }
}
