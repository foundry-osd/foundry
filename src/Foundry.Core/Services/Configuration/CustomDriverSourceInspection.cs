// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Services.Configuration;

public enum CustomDriverSourceState { Empty, Pending, Ready, Missing, NoDrivers, Inaccessible, TimedOut }

public sealed record CustomDriverSourceInspection(string Path, CustomDriverSourceState State, string? ErrorCode);
