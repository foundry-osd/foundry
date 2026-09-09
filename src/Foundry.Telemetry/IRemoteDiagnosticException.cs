// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Telemetry;

/// <summary>
/// Supplies a technical explanation without copying arbitrary payloads from the local exception message.
/// The remote policy still sanitizes and bounds this text before export.
/// </summary>
public interface IRemoteDiagnosticException
{
    string RemoteDiagnosticMessage { get; }
}
