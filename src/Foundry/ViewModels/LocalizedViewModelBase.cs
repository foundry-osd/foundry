using CommunityToolkit.Mvvm.ComponentModel;
using Foundry.Services.Localization;
using Microsoft.UI.Dispatching;

namespace Foundry.ViewModels;

public abstract class LocalizedViewModelBase : ObservableObject, IDisposable
{
    private readonly ILocalizationService _localizationService;
    private readonly DispatcherQueue? _dispatcherQueue;
    private bool _isDisposed;

    protected LocalizedViewModelBase(ILocalizationService localizationService)
    {
        _localizationService = localizationService ?? throw new ArgumentNullException(nameof(localizationService));
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
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

        if (_dispatcherQueue is null || _dispatcherQueue.HasThreadAccess)
        {
            action();
            return;
        }

        _dispatcherQueue.TryEnqueue(() => action());
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        RunOnUiThread(() => OnPropertyChanged(nameof(Strings)));
    }
}
