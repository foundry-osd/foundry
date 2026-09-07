// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Services.Configuration;

namespace Foundry.Services.Configuration;

/// <summary>Owns cached authoring readiness independently from any page's lifetime.</summary>
public interface ICustomDriverReadinessService : IDisposable
{
    CustomDriverSourceInspection Current { get; }
    event EventHandler? Changed;
    void RequestInspection(string? path);
    /// <summary>Permanently stops admission; cancelling the wait does not release a still-running scan.</summary>
    Task StopAsync(CancellationToken waitToken = default);
}
