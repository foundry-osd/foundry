// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Connect.Startup;

/// <summary>
/// The Foundry.Connect lifecycle stages whose failures map to distinct process exit codes through
/// <see cref="ConnectStartupFailurePolicy"/>.
/// </summary>
public enum ConnectStartupStage
{
    /// <summary>Loading and validating the runtime configuration.</summary>
    Configuration,

    /// <summary>Building the application host and starting its services.</summary>
    Services,

    /// <summary>Constructing and initializing the UI until it reports readiness to Foundry.Bootstrap.</summary>
    UiStartup,

    /// <summary>The UI reported readiness; any later failure is a runtime failure.</summary>
    Running
}
