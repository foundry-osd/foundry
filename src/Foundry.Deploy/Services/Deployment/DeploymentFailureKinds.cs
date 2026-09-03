// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Deploy.Services.Deployment;

/// <summary>
/// Defines stable deployment failure categories used by telemetry.
/// </summary>
public static class DeploymentFailureKinds
{
    public const string Validation = "validation";
    public const string Process = "process";
    public const string Http = "http";
    public const string Io = "io";
    public const string Timeout = "timeout";
    public const string Cancelled = "cancelled";
    public const string Cryptography = "cryptography";
    public const string Busy = "busy";
    public const string Unexpected = "unexpected";
}
