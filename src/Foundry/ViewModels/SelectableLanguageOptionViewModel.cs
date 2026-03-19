using CommunityToolkit.Mvvm.ComponentModel;
using Foundry.Models.Configuration;

namespace Foundry.ViewModels;

public partial class SelectableLanguageOptionViewModel : ObservableObject
{
    public SelectableLanguageOptionViewModel(LanguageRegistryEntry language)
    {
        Language = language;
    }

    public LanguageRegistryEntry Language { get; }
    public string Code => Language.Code;
    public string DisplayName => Language.DisplayName;

    [ObservableProperty]
    private bool isSelected;
}
