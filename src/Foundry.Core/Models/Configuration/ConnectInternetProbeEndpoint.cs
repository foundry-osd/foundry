// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Models.Configuration;

/// <summary>Defines the exact HTTP status and UTF-8 response expected from a connectivity probe.</summary>
public sealed record ConnectInternetProbeEndpoint(string Uri, int ExpectedStatusCode, string ExpectedBody)
{
    public static ConnectInternetProbeEndpoint Microsoft { get; } =
        new("http://www.msftconnecttest.com/connecttest.txt", 200, "Microsoft Connect Test");
}
