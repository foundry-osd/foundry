using CommunityToolkit.Mvvm.Input;

namespace Foundry.Connect.ViewModels;

public sealed partial class AboutDialogViewModel
{
    public AboutDialogViewModel(
        string aboutTitle,
        string appName,
        string version,
        string descriptionLine1,
        string descriptionLine2,
        string footer)
    {
        AboutTitle = aboutTitle;
        AppName = appName;
        Version = version;
        DescriptionLine1 = descriptionLine1;
        DescriptionLine2 = descriptionLine2;
        Footer = footer;
    }

    public string AboutTitle { get; }

    public string AppName { get; }

    public string Version { get; }

    public string DescriptionLine1 { get; }

    public string DescriptionLine2 { get; }

    public string Footer { get; }

    public event EventHandler? CloseRequested;

    [RelayCommand]
    private void Close()
    {
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }
}
