// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Foundry.Localization;
using Foundry.Wpf.Localization;

namespace Foundry.Wpf.ViewModels;

/// <summary>
/// Base class for view models bound to localized strings. It raises a change notification for
/// <see cref="Strings"/> whenever the active language changes so bindings refresh without a restart.
/// </summary>
/// <remarks>
/// The instance subscribes to <see cref="ILocalizationService.LanguageChanged"/> for its whole lifetime;
/// dispose it when the owning view goes away, otherwise the localization service keeps it alive.
/// </remarks>
public abstract class LocalizedViewModelBase : ObservableObject, IDisposable
{
    private readonly ILocalizationService _localizationService;
    private readonly Dispatcher _dispatcher;
    private bool _isDisposed;

    /// <summary>Subscribes to language changes and captures the dispatcher used to marshal notifications.</summary>
    /// <param name="localizationService">Source of the localized strings and language change events.</param>
    /// <exception cref="ArgumentNullException"><paramref name="localizationService"/> is <see langword="null"/>.</exception>
    protected LocalizedViewModelBase(ILocalizationService localizationService)
    {
        _localizationService = localizationService ?? throw new ArgumentNullException(nameof(localizationService));
        _dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        _localizationService.LanguageChanged += OnLanguageChanged;
    }

    /// <summary>Localized strings for the active language; raises a change notification on language switch.</summary>
    public LocalizedStrings Strings => _localizationService.Strings;

    /// <summary>Localization service for derived view models that format or resolve strings themselves.</summary>
    protected ILocalizationService LocalizationService => _localizationService;

    /// <summary>Unsubscribes from language changes. Idempotent; overrides must call the base implementation.</summary>
    public virtual void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _localizationService.LanguageChanged -= OnLanguageChanged;
        _isDisposed = true;
    }

    /// <summary>
    /// Runs <paramref name="action"/> synchronously on the UI dispatcher captured at construction, inline when
    /// already on that thread. Blocks the caller while the dispatcher executes the action.
    /// </summary>
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
