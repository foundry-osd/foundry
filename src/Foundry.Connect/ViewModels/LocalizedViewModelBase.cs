// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using CommunityToolkit.Mvvm.ComponentModel;
using Foundry.Avalonia.Services.Threading;
using Foundry.Connect.Services.Localization;
using Foundry.Localization;

namespace Foundry.Connect.ViewModels;

public abstract class LocalizedViewModelBase : ObservableObject, IDisposable
{
    private readonly ILocalizationService _localizationService;
    private readonly IUiDispatcher _dispatcher;
    private bool _isDisposed;

    protected LocalizedViewModelBase(ILocalizationService localizationService, IUiDispatcher dispatcher)
    {
        _localizationService = localizationService ?? throw new ArgumentNullException(nameof(localizationService));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _localizationService.LanguageChanged += OnLanguageChanged;
    }

    public LocalizedStrings Strings => _localizationService.Strings;

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

        _dispatcher.Post(action);
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        RunOnUiThread(() => OnPropertyChanged(nameof(Strings)));
    }
}
