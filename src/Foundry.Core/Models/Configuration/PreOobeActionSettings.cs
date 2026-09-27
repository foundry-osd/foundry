// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Models.Configuration;

public enum PreOobeActionKind { PowerShell, Command, Application, Restart }
public enum PreOobeApplicationMode { Exe, Msi }
public enum PreOobeErrorPolicy { Stop, Continue }
public enum PreOobeRestartTiming { Immediate, Deferred }

/// <summary>References immutable imported content; display metadata never determines its identity.</summary>
public sealed record PreOobePackageReference
{
    public string ContentHash { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public long Length { get; init; }
    public int FileCount { get; init; }
}

/// <summary>Defines child-process policy independently from Windows Setup's restart return codes.</summary>
public sealed record PreOobeProcessSettings
{
    public int TimeoutSeconds { get; init; } = 1800;
    public PreOobeErrorPolicy ErrorPolicy { get; init; }
    public IReadOnlyList<int> SuccessExitCodes { get; init; } = [0];
    public IReadOnlyList<int> RestartExitCodes { get; init; } = [];
    public PreOobeRestartTiming RestartTiming { get; init; }
}

/// <summary>Defines one administrator-owned action. Array position determines order; IDs remain stable when reordered.</summary>
public sealed record PreOobeActionSettings
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Name { get; init; } = string.Empty;
    public bool IsEnabled { get; init; } = true;
    public PreOobeActionKind Kind { get; init; }
    public PreOobePackageReference? Package { get; init; }
    public string? EntryPoint { get; init; }
    public string? Arguments { get; init; }
    public string? Command { get; init; }
    /// <summary>Null selects the package root; a supplied directory is package-relative.</summary>
    public string? WorkingDirectory { get; init; }
    public PreOobeApplicationMode? ApplicationMode { get; init; }
    public PreOobeProcessSettings? Process { get; init; }

    /// <summary>Creates type-specific defaults; Restart deliberately has no child-process policy.</summary>
    public static PreOobeActionSettings Create(PreOobeActionKind kind, string name) => new()
    {
        Kind = kind,
        Name = name,
        ApplicationMode = kind == PreOobeActionKind.Application ? PreOobeApplicationMode.Exe : null,
        Process = kind == PreOobeActionKind.Restart ? null : new()
        {
            RestartExitCodes = kind == PreOobeActionKind.Application ? [3010] : []
        }
    };
}
