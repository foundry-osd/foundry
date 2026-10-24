// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Configuration;
using Foundry.Core.Services.Files;
using Serilog;

namespace Foundry.Services.Configuration;

/// <summary>
/// Deletes authoring library files, such as replaced certificates or removed answer files that may hold credentials, once the
/// configuration no longer references them.
/// </summary>
/// <remarks>
/// A cleanup runs whenever a configuration change drops a file reference (an 802.1X or Wi-Fi template or certificate, the
/// Autopilot boot media certificate or an answer file), including a replacement or an import, and once after the legacy migration.
/// A file is kept while <see cref="IFoundryConfigurationStateService.Current"/> or
/// <see cref="IFoundryConfigurationStateService.SavedConfiguration"/> references it, while a media build snapshot leases it, or
/// when it was imported after the references were read. Failures are logged and retried by the next cleanup; file contents are
/// never logged. Cleanups never overlap: a request during a run schedules one more run.
/// </remarks>
internal sealed class AuthoringFileCleanupService : IDisposable
{
    private readonly IFoundryConfigurationStateService configuration;
    private readonly AuthoringFileLibrary library;
    private readonly ILogger logger;
    private readonly Lock gate = new();
    private IReadOnlySet<string> knownReferences;
    private bool isRunning;
    private bool isRerunRequested;

    public AuthoringFileCleanupService(IFoundryConfigurationStateService configuration, AuthoringFileLibrary library, ILogger logger)
    {
        this.configuration = configuration;
        this.library = library;
        this.logger = logger.ForContext<AuthoringFileCleanupService>();
        knownReferences = ConfigurationContentReferences.AuthoringFileHashes(configuration.Current);
        configuration.StateChanged += OnStateChanged;
    }

    /// <summary>Starts a cleanup, or schedules one more when a cleanup is already running.</summary>
    public void RequestCleanup()
    {
        lock (gate)
        {
            if (isRunning)
            {
                isRerunRequested = true;
                return;
            }

            isRunning = true;
        }

        _ = RunAsync();
    }

    public void Dispose() => configuration.StateChanged -= OnStateChanged;

    private void OnStateChanged(object? sender, EventArgs e)
    {
        IReadOnlySet<string> current = ConfigurationContentReferences.AuthoringFileHashes(configuration.Current);
        bool referenceDropped = knownReferences.Any(hash => !current.Contains(hash));
        knownReferences = current;
        if (referenceDropped) RequestCleanup();
    }

    private async Task RunAsync()
    {
        while (true)
        {
            // Read the references and the import sequence together, on the caller's thread, before deleting anything.
            FoundryConfigurationDocument current = configuration.Current;
            FoundryConfigurationDocument saved = configuration.SavedConfiguration;
            var references = new HashSet<string>(ConfigurationContentReferences.AuthoringFileHashes(current), StringComparer.OrdinalIgnoreCase);
            references.UnionWith(ConfigurationContentReferences.AuthoringFileHashes(saved));
            long importSequence = library.ImportSequence;

            logger.Information("Authoring file cleanup started. ReferencedCount={ReferencedCount}", references.Count);
            try
            {
                AuthoringFileCleanupResult result = await Task.Run(() => library.DeleteUnreferenced(references, importSequence));
                if (result.Failed > 0)
                {
                    logger.Warning(
                        "Authoring file cleanup could not delete every unreferenced file; they are retried at the next cleanup. RemovedCount={RemovedCount}, KeptCount={KeptCount}, FailedCount={FailedCount}",
                        result.Removed, result.Kept, result.Failed);
                }
                else
                {
                    logger.Information(
                        "Authoring file cleanup completed. RemovedCount={RemovedCount}, KeptCount={KeptCount}",
                        result.Removed, result.Kept);
                }
            }
            catch (Exception exception)
            {
                logger.Warning(exception, "Authoring file cleanup failed; unreferenced files are retried at the next cleanup.");
            }

            lock (gate)
            {
                if (!isRerunRequested)
                {
                    isRunning = false;
                    return;
                }

                isRerunRequested = false;
            }
        }
    }
}
