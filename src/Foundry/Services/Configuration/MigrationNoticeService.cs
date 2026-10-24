// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Services.Configuration.Legacy;
using Foundry.Core.Services.Secrets;
using Foundry.Services.Secrets;
using Serilog;

namespace Foundry.Services.Configuration;

/// <summary>
/// Shows each migration notice of the current Windows user until it is dismissed; an unreadable secret vault also shows the
/// passwords notice for the session.
/// </summary>
/// <remarks>The pending notices are read once from the per-user <see cref="LegacyMigrationStateFile"/>, after the migration ran.</remarks>
internal sealed class MigrationNoticeService : IMigrationNoticeService, IDisposable
{
    private readonly LegacyMigrationStateFile stateFile;
    private readonly ISecretVaultBridge vault;
    private readonly ILogger logger;
    private LegacyMigrationState? state;
    private bool isVaultNoticeDismissed;

    public MigrationNoticeService(LegacyMigrationStateFile stateFile, ISecretVaultBridge vault, ILogger logger)
    {
        this.stateFile = stateFile;
        this.vault = vault;
        this.logger = logger.ForContext<MigrationNoticeService>();
        vault.StatusChanged += OnVaultStatusChanged;
    }

    /// <inheritdoc />
    public event EventHandler? Changed;

    /// <inheritdoc />
    public bool IsShown(MigrationNotice notice) =>
        State.PendingNotices.Contains(notice) ||
        (notice == MigrationNotice.PasswordsNeeded && vault.Status == VaultStatus.Unreadable && !isVaultNoticeDismissed);

    /// <inheritdoc />
    public void Dismiss(MigrationNotice notice)
    {
        if (notice == MigrationNotice.PasswordsNeeded) isVaultNoticeDismissed = true;
        LegacyMigrationState current = State;
        if (current.PendingNotices.Contains(notice))
        {
            state = current with { PendingNotices = current.PendingNotices.Where(pending => pending != notice).ToArray() };
            try
            {
                stateFile.Save(state);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                logger.Warning(exception, "Dismissed migration notice could not be recorded; it is shown again at the next launch. Notice={Notice}", notice);
            }
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose() => vault.StatusChanged -= OnVaultStatusChanged;

    private LegacyMigrationState State => state ??= LoadState();

    private LegacyMigrationState LoadState()
    {
        try
        {
            return stateFile.Load();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.Warning(exception, "Migration notices could not be read and are not shown during this session.");
            return new LegacyMigrationState();
        }
    }

    private void OnVaultStatusChanged(object? sender, EventArgs e) => Changed?.Invoke(this, EventArgs.Empty);
}
