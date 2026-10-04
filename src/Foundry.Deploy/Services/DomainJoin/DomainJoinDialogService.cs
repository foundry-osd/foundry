// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Windows;
using Foundry.Core.Models.Configuration.Deploy;
using Foundry.Deploy.Services.Localization;
using Foundry.Deploy.Views;

namespace Foundry.Deploy.Services.DomainJoin;

/// <summary>Shows the owned-input dialog on the launch UI thread before disk confirmation.</summary>
public sealed class DomainJoinDialogService(ILocalizationService localization) : IDomainJoinDialogService
{
    public DomainJoinDialogResult? Show(DeployDomainJoinSettings settings, bool requiresCredentials)
    {
        var dialog = new DomainJoinDialog(settings, requiresCredentials, localization)
        {
            Owner = Application.Current?.Windows.OfType<Window>().FirstOrDefault(window => window.IsActive)
                ?? Application.Current?.MainWindow
        };
        try
        {
            return dialog.ShowDialog() == true ? dialog.TakeResult() : null;
        }
        finally
        {
            dialog.TakeResult()?.Dispose();
        }
    }
}
