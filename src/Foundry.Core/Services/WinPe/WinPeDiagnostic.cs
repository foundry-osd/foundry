// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Services.WinPe;

public sealed record WinPeDiagnostic(
    string Code,
    string Message,
    string? Details = null,
    string? Stage = null,
    string? Command = null,
    int? ExitCode = null);
