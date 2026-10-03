// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Deploy.Models.Configuration;
using BootMediaUpdateReason = Foundry.Core.Models.Configuration.BootMediaUpdateReason;

namespace Foundry.Deploy.Services.Configuration;

public sealed record DeployConfigurationLoadResult
{
    public string ConfigurationPath { get; init; } = DeployConfigurationService.DefaultConfigurationPath;
    public bool Exists { get; init; }
    public FoundryDeployConfigurationDocument? Document { get; init; }

    /// <summary>Gets why rebuilding this boot media is recommended.</summary>
    public BootMediaUpdateReason BootMediaUpdateReason { get; init; }

    public bool IsBootMediaUpdateRecommended => BootMediaUpdateReason != BootMediaUpdateReason.None;
    public string? FailureMessage { get; init; }

    /// <summary>Preserves the original parse or validation exception for protected startup diagnostics.</summary>
    public Exception? FailureException { get; init; }
}
