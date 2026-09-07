// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Services.Adk;

/// <summary>Owns an elevated setup process independently of request cancellation.</summary>
public interface IAdkInstallerRunner
{
    /// <summary>Gets the actual native wait, retained until confirmed exit.</summary>
    Task? ActiveOperation { get; }

    /// <summary>Starts verified setup and observes its bounded terminal outcome without killing installers.</summary>
    Task<AdkInstallerExecution> RunAsync(string executablePath, IReadOnlyList<string> arguments, CancellationToken cancellationToken);
}

/// <summary>Records native completion and process identity without equating cancellation with exit.</summary>
public sealed record AdkInstallerExecution(int? ExitCode, bool HasExited, bool CancellationRequested, bool OwnershipUncertain)
{
    /// <summary>Gets the started setup process identifier when known.</summary>
    public int? ProcessId { get; init; }

    /// <summary>Gets its creation time for recovery identity checks.</summary>
    public DateTimeOffset? StartTimeUtc { get; init; }
}
