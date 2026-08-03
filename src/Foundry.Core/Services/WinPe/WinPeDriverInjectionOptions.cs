// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Services.WinPe;

public sealed record WinPeDriverInjectionOptions
{
    public string MountedImagePath { get; init; } = string.Empty;
    public IReadOnlyList<string> DriverPackagePaths { get; init; } = [];
    public bool RecurseSubdirectories { get; init; } = true;
    public string? DismExecutablePath { get; init; }
    public string WorkingDirectoryPath { get; init; } = string.Empty;
    public IProgress<WinPeDismProgress>? DismProgress { get; init; }

    /// <summary>
    /// Gets a value indicating whether a driver package that fails to inject is skipped (with a warning)
    /// so remaining packages continue, instead of failing the whole operation.
    /// </summary>
    public bool ContinueOnError { get; init; }
}
