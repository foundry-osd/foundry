// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration.Deploy;
using Foundry.Deploy.Services.Images;
using Foundry.Deploy.Services.Localization;
using Foundry.Deploy.Services.Runtime;

namespace Foundry.Deploy.ViewModels;

public partial class CustomImageSelectionViewModel
{
    private DeployCustomImagesSettings _configuredSettings = new();
    private DebugCustomImageSnapshot? _debugSnapshot;

    public bool IsDebugScenarioActive => _debugSnapshot is not null;
    public string DebugScenarioText => _debugSnapshot is null ? string.Empty :
        LocalizationText.Format("Debug.CustomImages.ActiveScenario", LocalizationText.GetString("Debug.CustomImages." + _debugSnapshot.Scenario));

    /// <summary>Overrides only in-memory selection data; restoring configuration resumes normal media discovery.</summary>
    public async Task SetDebugScenarioAsync(DebugCustomImageScenario scenario)
    {
        if (!DebugSafetyMode.IsEnabled || !Enum.IsDefined(scenario) || _lifetime.IsCancellationRequested) return;
        if (scenario == DebugCustomImageScenario.Configured) Configure(_configuredSettings);
        else
        {
            _debugSnapshot = DebugCustomImageScenarios.Create(scenario);
            ConfigureSelection(_debugSnapshot.Settings);
            NotifyDebugScenario();
        }
        await RefreshAsync();
    }

    private static CustomImageCatalogResult GetDebugCatalog(DebugCustomImageSnapshot scenario)
    {
        if (!DebugSafetyMode.IsEnabled) throw new InvalidOperationException("Debug image scenarios require an attached debugger in a Debug build.");
        return new(scenario.Images, scenario.CatalogErrorKey);
    }

    private void NotifyDebugScenario()
    {
        OnPropertyChanged(nameof(IsDebugScenarioActive));
        OnPropertyChanged(nameof(DebugScenarioText));
    }
}
