using Foundry.Services.Localization;
using Microsoft.UI.Xaml.Controls;

namespace Foundry.Behaviors;

public static class LocalizedToggleSwitch
{
    public static readonly DependencyProperty UseLocalizedStateContentProperty =
        DependencyProperty.RegisterAttached(
            "UseLocalizedStateContent",
            typeof(bool),
            typeof(LocalizedToggleSwitch),
            new PropertyMetadata(false, OnUseLocalizedStateContentChanged));

    private static readonly DependencyProperty StateProperty =
        DependencyProperty.RegisterAttached(
            "State",
            typeof(LocalizedToggleSwitchState),
            typeof(LocalizedToggleSwitch),
            new PropertyMetadata(null));

    public static bool GetUseLocalizedStateContent(DependencyObject obj)
    {
        return (bool)obj.GetValue(UseLocalizedStateContentProperty);
    }

    public static void SetUseLocalizedStateContent(DependencyObject obj, bool value)
    {
        obj.SetValue(UseLocalizedStateContentProperty, value);
    }

    private static void OnUseLocalizedStateContentChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is not ToggleSwitch toggleSwitch)
        {
            return;
        }

        if (e.NewValue is true)
        {
            Attach(toggleSwitch);
            return;
        }

        Detach(toggleSwitch);
    }

    private static void Attach(ToggleSwitch toggleSwitch)
    {
        if (toggleSwitch.GetValue(StateProperty) is LocalizedToggleSwitchState)
        {
            return;
        }

        LocalizedToggleSwitchState state = new(toggleSwitch);
        toggleSwitch.SetValue(StateProperty, state);
        state.Attach();
    }

    private static void Detach(ToggleSwitch toggleSwitch)
    {
        if (toggleSwitch.GetValue(StateProperty) is LocalizedToggleSwitchState state)
        {
            state.Detach();
            toggleSwitch.ClearValue(StateProperty);
        }
    }

    private sealed class LocalizedToggleSwitchState(ToggleSwitch toggleSwitch)
    {
        private IApplicationLocalizationService? localizationService;

        public void Attach()
        {
            toggleSwitch.Loaded += OnLoaded;
            toggleSwitch.Unloaded += OnUnloaded;
        }

        public void Detach()
        {
            toggleSwitch.Loaded -= OnLoaded;
            toggleSwitch.Unloaded -= OnUnloaded;

            if (localizationService is not null)
            {
                localizationService.LanguageChanged -= OnLanguageChanged;
                localizationService = null;
            }
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            localizationService = App.GetService<IApplicationLocalizationService>();
            ApplyLocalizedContent();
            localizationService.LanguageChanged += OnLanguageChanged;
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            if (localizationService is not null)
            {
                localizationService.LanguageChanged -= OnLanguageChanged;
                localizationService = null;
            }
        }

        private void OnLanguageChanged(object? sender, ApplicationLanguageChangedEventArgs e)
        {
            if (toggleSwitch.DispatcherQueue.HasThreadAccess)
            {
                ApplyLocalizedContent();
                return;
            }

            toggleSwitch.DispatcherQueue.TryEnqueue(ApplyLocalizedContent);
        }

        private void ApplyLocalizedContent()
        {
            if (localizationService is null)
            {
                return;
            }

            string enabledText = localizationService.GetString("Common.Enabled");
            string disabledText = localizationService.GetString("Common.Disabled");

            toggleSwitch.OnContent = enabledText;
            toggleSwitch.OffContent = disabledText;
        }
    }
}
