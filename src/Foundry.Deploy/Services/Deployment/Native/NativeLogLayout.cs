// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO;

namespace Foundry.Deploy.Services.Deployment.Native;

/// <summary>
/// Defines where native servicing logs live, so the worker requests that write them and the diagnostic handoff that
/// preserves them share one definition.
/// </summary>
internal static class NativeLogLayout
{
    /// <summary>Returns the durable native log directory beneath a Foundry staging root.</summary>
    public static string GetDirectory(string foundryRoot) => Path.Combine(foundryRoot, "Logs", "Native");

    /// <summary>
    /// Resolves the staging root that owns a <c>&lt;root&gt;\Temp\&lt;name&gt;</c> working directory. Any other shape
    /// returns <see langword="null"/> because no durable log location can be derived from it.
    /// </summary>
    public static string? TryResolveFoundryRoot(string workingDirectory)
    {
        DirectoryInfo? temp = Directory.GetParent(Path.GetFullPath(workingDirectory));
        return temp?.Name.Equals("Temp", StringComparison.OrdinalIgnoreCase) == true ? temp.Parent?.FullName : null;
    }
}
