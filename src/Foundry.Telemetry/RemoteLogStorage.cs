// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Telemetry;

/// <summary>Resolves a separate outbox for each application and destination without storing credentials.</summary>
internal static class RemoteLogStorage
{
    internal static string Resolve(RemoteDiagnosticsOptions options, RemoteDiagnosticsContext context)
    {
        string root = options.LogDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Foundry", "Diagnostics", "PendingLogs");
        root = ResolveRoot(root, context, Environment.GetEnvironmentVariable(
            Foundry.Utilities.Diagnostics.DiagnosticSessionContext.PersistenceDirectoryEnvironmentVariableName));
        string application = string.Concat(context.App.Select(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-' ? character : '_'));
        return Path.Combine(root, application,
            BootstrapTelemetryJournal.Scope(options.HostUrl, options.ProjectToken, options.InstallId));
    }

    /// <summary>Uses Bootstrap's persistent cache across boot sessions when its inherited session path matches.</summary>
    internal static string ResolveRoot(string fallback, RemoteDiagnosticsContext context, string? persistenceDirectory)
    {
        if (context.Runtime != TelemetryRuntimeModes.WinPe || string.IsNullOrWhiteSpace(persistenceDirectory)) return fallback;
        try
        {
            if (!Path.IsPathFullyQualified(persistenceDirectory) || persistenceDirectory.StartsWith(@"\\", StringComparison.Ordinal)) return fallback;
            string path = Path.GetFullPath(persistenceDirectory).TrimEnd(Path.DirectorySeparatorChar);
            if (!string.Equals(Path.GetFileName(path), context.SessionId, StringComparison.OrdinalIgnoreCase)) return fallback;
            string? parent = Path.GetDirectoryName(path);
            return parent is null ? fallback : Path.Combine(parent, "PendingLogs");
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException) { return fallback; }
    }
}
