// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Deploy.Services.Deployment;

/// <summary>States relevant to offline deployment policy, including changes pending Windows startup.</summary>
public enum OfflineWindowsFeatureState
{
    Disabled,
    PayloadRemoved,
    Enabled,
    EnablePending,
    DisablePending
}
