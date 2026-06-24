// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Connect.Services.Configuration;

public sealed class FoundryConnectConfigurationException : Exception
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
