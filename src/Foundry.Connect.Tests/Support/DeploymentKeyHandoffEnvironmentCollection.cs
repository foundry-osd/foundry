// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Connect.Tests.Support;

/// <summary>
/// Serializes the tests that set the process-wide deployment key hand-off variable with the tests that construct the
/// sender, which removes it.
/// </summary>
[CollectionDefinition(nameof(DeploymentKeyHandoffEnvironmentCollection), DisableParallelization = true)]
public sealed class DeploymentKeyHandoffEnvironmentCollection;
