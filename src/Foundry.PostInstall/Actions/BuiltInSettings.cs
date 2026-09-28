// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Text.Json;
using Foundry.Core.Models.PreOobe;
using Foundry.PostInstall.Execution;

namespace Foundry.PostInstall.Actions;

internal sealed record DriverSettings(string CommandKind, string PackagePath);
internal sealed record AppxSettings(IReadOnlyList<string> PackageNames);
internal sealed record NetworkSettings(string SettingsPath);

internal static class BuiltInSettings
{
    public static T Read<T>(PreOobeExecutionAction action) where T : class => action.Parameters?.Deserialize<T>(ExecutionJournal.JsonOptions)
        ?? throw new InvalidDataException("Built-in settings are missing.");

    public static ActionStepOutcome Observe(ProcessOutcome outcome, int? next = null) => new(
        outcome.ExitCode is 0 or 3010 && !outcome.TerminationUncertain,
        outcome.ExitCode, outcome.TerminationUncertain ? "execution_uncertain" : outcome.ExitCode is 0 or 3010 ? null : "process_failed",
        RestartRequested: outcome.ExitCode == 3010, TerminationUncertain: outcome.TerminationUncertain,
        NextSubstep: outcome.ExitCode is 0 or 3010 && !outcome.TerminationUncertain ? next : null);
}
