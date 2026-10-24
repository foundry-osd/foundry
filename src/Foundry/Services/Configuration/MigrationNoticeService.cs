// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Services.Configuration.Legacy;
using Foundry.Core.Services.Secrets;
using Foundry.Services.Secrets;
using Foundry.Services.Settings;
using Serilog;

namespace Foundry.Services.Configuration;

/// <summary>
/// Shows each migration notice until it is dismissed; an unreadable secret vault also shows the passwords notice for the session.
/// </summary>
internal sealed class MigrationNoticeService : IMigrationNoticeService, IDisposable
{
    private readonly IAppSettingsService settings;
    private readonly ISecretVaultBridge vault;
    private readonly ILogger logger;
    private bool isVaultNoticeDismissed;

    public MigrationNoticeService(IAppSettingsService settings, ISecretVaultBridge vault, ILogger logger)
    {
        this.settings = settings;
        this.vault = vault;
        this.logger = logger.ForContext<MigrationNoticeService>();
        vault.StatusChanged += OnVaultStatusChanged;
    }

    /// <inheritdoc />
    public event EventHandler? Changed;

    /// <inheritdoc />
    public bool IsShown(MigrationNotice notice) =>
        settings.Current.Migrations.PendingNotices.Contains(notice) ||
        (notice == MigrationNotice.PasswordsNeeded && vault.Status == VaultStatus.Unreadable && !isVaultNoticeDismissed);

    /// <inheritdoc />
    public void Dismiss(MigrationNotice notice)
    {
        if (notice == MigrationNotice.PasswordsNeeded) isVaultNoticeDismissed = true;
        if (settings.Current.Migrations.PendingNotices.Remove(notice))
        {
            try
            {
                settings.Save();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                logger.Warning(exception, "Dismissed migration notice could not be recorded; it is shown again at the next launch. Notice={Notice}", notice);
            }
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose() => vault.StatusChanged -= OnVaultStatusChanged;

    private void OnVaultStatusChanged(object? sender, EventArgs e) => Changed?.Invoke(this, EventArgs.Empty);
}
