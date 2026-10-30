// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Connect.Configuration;

/// <summary>Signals a configuration problem inside the loader; <see cref="ConnectConfigurationService.Load"/> converts it to a failed load result.</summary>
internal sealed class FoundryConnectConfigurationException : Exception
{
    public FoundryConnectConfigurationException(string message)
        : base(message)
    {
    }

    public FoundryConnectConfigurationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
