// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Wpf.Localization;

/// <summary>
/// Reports the UI culture of a localization service to a host hook: once when started, then after each language change.
/// </summary>
public sealed class CultureChangeReporter : IDisposable
{
    private readonly ILocalizationService _localization;
    private readonly Action<string> _report;

    private CultureChangeReporter(ILocalizationService localization, Action<string> report)
    {
        _localization = localization;
        _report = report;
    }

    /// <summary>Reports the current culture, then follows <see cref="ILocalizationService.LanguageChanged"/>.</summary>
    /// <param name="localization">The service whose culture is reported.</param>
    /// <param name="report">The host hook, called with the culture name; an exception it throws is swallowed.</param>
    /// <returns>The reporter; dispose it to stop following the service.</returns>
    public static CultureChangeReporter Start(ILocalizationService localization, Action<string> report)
    {
        ArgumentNullException.ThrowIfNull(localization);
        ArgumentNullException.ThrowIfNull(report);

        var reporter = new CultureChangeReporter(localization, report);
        localization.LanguageChanged += reporter.OnLanguageChanged;
        reporter.Report();
        return reporter;
    }

    /// <inheritdoc />
    public void Dispose() => _localization.LanguageChanged -= OnLanguageChanged;

    private void OnLanguageChanged(object? sender, Foundry.Localization.ApplicationLanguageChangedEventArgs e) => Report();

    private void Report()
    {
        try
        {
            _report(_localization.CurrentCulture.Name);
        }
        catch
        {
            // The hook belongs to the host, which logs its own failures; a language change must never fail because of it.
        }
    }
}
