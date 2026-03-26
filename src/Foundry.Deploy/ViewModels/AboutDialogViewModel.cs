using CommunityToolkit.Mvvm.Input;

namespace Foundry.Deploy.ViewModels;

public sealed partial class AboutDialogViewModel
{
    public string AppName => FoundryDeployApplicationInfo.AppName;

    public string Version => FoundryDeployApplicationInfo.Version;

    public string DescriptionLine1 => FoundryDeployApplicationInfo.DescriptionLine1;

    public string DescriptionLine2 => FoundryDeployApplicationInfo.DescriptionLine2;

    public string Footer => FoundryDeployApplicationInfo.Footer;

    public event EventHandler? CloseRequested;

    [RelayCommand]
    private void Close()
    {
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }
}
