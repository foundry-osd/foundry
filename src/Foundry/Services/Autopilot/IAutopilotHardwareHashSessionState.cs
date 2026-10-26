// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Autopilot;

namespace Foundry.Services.Autopilot;

/// <summary>
/// Stores Autopilot hardware hash state that is valid only for the current app session.
/// </summary>
public interface IAutopilotHardwareHashSessionState
{
    /// <summary>
    /// Gets or sets whether the tenant was connected during the current app session.
    /// </summary>
    bool HasConnectedTenant { get; set; }

    /// <summary>
    /// Gets or sets the last tenant onboarding status from the current app session.
    /// </summary>
    AutopilotTenantOnboardingStatus? TenantOnboardingStatus { get; set; }

    /// <summary>
    /// Gets or sets the certificate credentials discovered from Graph in the current app session.
    /// </summary>
    IReadOnlyList<AutopilotGraphKeyCredential> Certificates { get; set; }

    /// <summary>
    /// Occurs when the boot media PFX password or the file it is bound to changes; the secret vault bridge persists it.
    /// </summary>
    event EventHandler? BootMediaCertificatePasswordChanged;

    /// <summary>
    /// Gets the password of the boot media PFX, or <see langword="null"/> when none was entered for that file.
    /// </summary>
    /// <param name="pfx">The PFX selected in the configuration.</param>
    string? GetBootMediaCertificatePassword(AuthoringFileReference? pfx);

    /// <summary>
    /// Records the password of the boot media PFX, bound to its content hash so it never unlocks another file. It never enters
    /// the configuration; the secret vault bridge remembers it in the local vault.
    /// </summary>
    /// <param name="pfx">The PFX the password belongs to; <see langword="null"/> clears the password.</param>
    /// <param name="password">The password entered by the operator.</param>
    void SetBootMediaCertificatePassword(AuthoringFileReference? pfx, string? password);

    /// <summary>
    /// Clears tenant, certificate table, and boot media PFX password state without touching persisted settings.
    /// </summary>
    void ClearTenantConnection();
}
