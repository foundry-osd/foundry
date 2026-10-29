// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Models.Runtime;

/// <summary>
/// Process exit codes a boot-media child application returns to Foundry.Bootstrap. Bootstrap treats
/// <see cref="UserAborted"/> as operator cancellation and maps the failure codes to specific console messages; any
/// other value is reported generically. Numeric values are a cross-process contract and must not change.
/// </summary>
public enum ChildRuntimeExitCode
{
    /// <summary>The child completed its work.</summary>
    Success = 0,

    /// <summary>The operator deliberately aborted the child; Bootstrap stops the boot without reporting a failure.</summary>
    UserAborted = 20,

    /// <summary>The child could not start, for example because the shell or a required service failed to initialize.</summary>
    StartupFailure = 21,

    /// <summary>The child could not load or validate its runtime configuration.</summary>
    ConfigurationFailure = 22,

    /// <summary>The child stopped unexpectedly after it had started.</summary>
    RuntimeFailure = 23
}
