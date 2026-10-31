// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Tests.Runtime;

/// <summary>Serializes tests that touch the process-wide remote diagnostics sink.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class RuntimeStartupDiagnosticsCollection
{
    public const string Name = "RuntimeStartupDiagnostics";
}
