// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Connect.ViewModels;

public sealed record ProvisionedWifiPresentation(
    bool IsConfigured,
    bool ShowDetails,
    bool IsConnected,
    bool IsActionInProgress,
    string ProfileName,
    string Authentication,
    string Source,
    string Status,
    string Placeholder,
    string Feedback)
{
    public bool ShowPlaceholder => !ShowDetails;

    public bool ShowConnectAction => ShowDetails && !IsConnected;

    public bool ShowDisconnectAction => ShowDetails && IsConnected;

    public bool HasFeedback => !string.IsNullOrWhiteSpace(Feedback);
}
