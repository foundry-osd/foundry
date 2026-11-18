// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Globalization;
using Foundry.Localization;
using Foundry.Wpf.Localization;

namespace Foundry.Wpf.Tests.Localization;

public sealed class CultureChangeReporterTests
{
    [Fact]
    public void Start_ReportsTheCurrentCultureThenEachChangeUntilDisposed()
    {
        var service = new FakeLocalizationService("en-US");
        var reported = new List<string>();

        CultureChangeReporter reporter = CultureChangeReporter.Start(service, reported.Add);
        service.Change("fr-FR");
        reporter.Dispose();
        service.Change("de-DE");

        Assert.Equal(["en-US", "fr-FR"], reported);
    }

    [Fact]
    public void AThrowingHook_NeverFailsTheLanguageChange()
    {
        var service = new FakeLocalizationService("en-US");

        using CultureChangeReporter reporter = CultureChangeReporter.Start(service, _ => throw new InvalidOperationException());
        service.Change("fr-FR");

        Assert.Equal("fr-FR", service.CurrentCulture.Name);
    }

    private sealed class FakeLocalizationService(string culture) : ILocalizationService
    {
        public CultureInfo CurrentCulture { get; private set; } = CultureInfo.GetCultureInfo(culture);

        public LocalizedStrings Strings => throw new NotSupportedException();

        public event EventHandler<ApplicationLanguageChangedEventArgs>? LanguageChanged;

        public void Change(string name)
        {
            string old = CurrentCulture.Name;
            CurrentCulture = CultureInfo.GetCultureInfo(name);
            LanguageChanged?.Invoke(this, new ApplicationLanguageChangedEventArgs(old, name));
        }

        public void SetCulture(CultureInfo culture) => throw new NotSupportedException();

        public string GetString(string key) => throw new NotSupportedException();

        public string Format(string key, params object[] args) => throw new NotSupportedException();

        public IReadOnlyList<SupportedCultureOption> CreateSupportedCultureOptions() => throw new NotSupportedException();
    }
}
