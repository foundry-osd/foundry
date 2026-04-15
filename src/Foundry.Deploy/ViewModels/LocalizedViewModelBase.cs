using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Foundry.Deploy.Services.Localization;

namespace Foundry.Deploy.ViewModels;

public abstract class LocalizedViewModelBase : ObservableObject, IDisposable
{
    private readonly ILocalizationService _localizationService;
    private readonly Dispatcher _dispatcher;
    private bool _isDisposed;

    protected LocalizedViewModelBase(ILocalizationService localizationService)
    {
        _localizationService = localizationService ?? throw new ArgumentNullException(nameof(localizationService));
        _dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
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

    protected void RunOnUiThread(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (_dispatcher.CheckAccess())
        {
            action();
            return;
        }

        _dispatcher.Invoke(action);
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        RunOnUiThread(() => OnPropertyChanged(nameof(Strings)));
    }
}
