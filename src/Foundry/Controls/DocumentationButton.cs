using Foundry.Core.Services.Application;
using Foundry.Services.Localization;
using Microsoft.UI.Xaml.Automation;

namespace Foundry.Controls;

public sealed partial class DocumentationButton : UserControl
{
    public static readonly DependencyProperty DocumentationUrlProperty = DependencyProperty.Register(
        nameof(DocumentationUrl),
        typeof(string),
        typeof(DocumentationButton),
        new PropertyMetadata(string.Empty));

    private readonly IApplicationLocalizationService localizationService;
    private readonly IExternalProcessLauncher externalProcessLauncher;
    private readonly Button button = new();
    private readonly TextBlock labelTextBlock = new();
    private bool isLocalizationSubscribed;

    public DocumentationButton()
    {
        localizationService = App.GetService<IApplicationLocalizationService>();
        externalProcessLauncher = App.GetService<IExternalProcessLauncher>();
        button.Content = CreateContent();
        Content = button;
        UpdateLocalizedText();

        button.Click += OnClick;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public string DocumentationUrl
    {
        get => (string)GetValue(DocumentationUrlProperty);
        set => SetValue(DocumentationUrlProperty, value);
    }

    private StackPanel CreateContent()
    {
        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = (double)Application.Current.Resources["FoundrySpace8"],
            Children =
            {
                new FontIcon { Glyph = "\uE7C3" },
                labelTextBlock
            }
        };
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (isLocalizationSubscribed)
        {
            return;
        }

        localizationService.LanguageChanged += OnLanguageChanged;
        isLocalizationSubscribed = true;
        UpdateLocalizedText();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (!isLocalizationSubscribed)
        {
            return;
        }

        localizationService.LanguageChanged -= OnLanguageChanged;
        isLocalizationSubscribed = false;
    }

    private void OnLanguageChanged(object? sender, ApplicationLanguageChangedEventArgs e)
    {
        UpdateLocalizedText();
    }

    private void UpdateLocalizedText()
    {
        string text = localizationService.GetString("Nav_DocumentationKey.Title");
        labelTextBlock.Text = text;
        AutomationProperties.SetName(button, text);
    }

    private async void OnClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(DocumentationUrl))
        {
            return;
        }

        await externalProcessLauncher.OpenUriAsync(new Uri(DocumentationUrl, UriKind.Absolute));
    }
}
