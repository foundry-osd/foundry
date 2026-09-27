// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;

namespace Foundry.Core.Services.Configuration;

/// <summary>Shares custom action argument composition between authoring previews and execution.</summary>
public static class PreOobeCommandLine
{
    public static string BuildArguments(PreOobeActionSettings action, string entryPath, string logRoot) => action.Kind switch
    {
        PreOobeActionKind.Command => "/c \"" + action.Command + "\"",
        PreOobeActionKind.PowerShell => Join(action.PowerShellArguments, "-File \"" + entryPath + "\"", action.Arguments),
        PreOobeActionKind.Application when action.ApplicationMode == PreOobeApplicationMode.Msi =>
            Join("/i \"" + entryPath + "\"", action.Arguments, action.GenerateInstallationLog
                ? "/l*v \"" + Path.Combine(logRoot, Path.GetFileNameWithoutExtension(entryPath) + ".log") + "\"" : null),
        PreOobeActionKind.Application => action.Arguments ?? string.Empty,
        _ => throw new InvalidOperationException("Action has no command line.")
    };

    private static string Join(params string?[] parts) => string.Join(" ", parts.Where(part => !string.IsNullOrEmpty(part)));
}
