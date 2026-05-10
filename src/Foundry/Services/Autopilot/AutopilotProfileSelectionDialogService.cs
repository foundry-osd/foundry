using Foundry.Core.Models.Configuration;
using Foundry.Services.Localization;

namespace Foundry.Services.Autopilot;

public sealed class AutopilotProfileSelectionDialogService(
    IApplicationLocalizationService localizationService) : IAutopilotProfileSelectionDialogService
{
    public async Task<IReadOnlyList<AutopilotProfileSettings>?> PickProfilesAsync(
        IReadOnlyList<AutopilotProfileSettings> availableProfiles)
    {
        ArgumentNullException.ThrowIfNull(availableProfiles);

        var viewModel = new AutopilotProfileSelectionDialogViewModel(localizationService, availableProfiles);
        var dialog = new AutopilotProfileSelectionDialog(viewModel)
        {
            XamlRoot = App.MainWindow.Content.XamlRoot
        };

        try
        {
            ContentDialogResult result = await dialog.ShowAsync();
            return result == ContentDialogResult.Primary
                ? viewModel.GetSelectedProfiles()
                : null;
        }
        finally
        {
            viewModel.Dispose();
        }
    }
}
