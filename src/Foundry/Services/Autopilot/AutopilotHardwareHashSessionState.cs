// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Autopilot;

namespace Foundry.Services.Autopilot;

internal sealed class AutopilotHardwareHashSessionState : IAutopilotHardwareHashSessionState
{
    private string? bootMediaCertificateHash;
    private string? bootMediaCertificatePassword;

    public bool HasConnectedTenant { get; set; }

    public AutopilotTenantOnboardingStatus? TenantOnboardingStatus { get; set; }

    public IReadOnlyList<AutopilotGraphKeyCredential> Certificates { get; set; } = [];

    public event EventHandler? BootMediaCertificatePasswordChanged;

    public string? GetBootMediaCertificatePassword(AuthoringFileReference? pfx) =>
        pfx is not null && string.Equals(bootMediaCertificateHash, pfx.ContentHash, StringComparison.OrdinalIgnoreCase)
            ? bootMediaCertificatePassword
            : null;

    public void SetBootMediaCertificatePassword(AuthoringFileReference? pfx, string? password)
    {
        string? hash = pfx?.ContentHash;
        string? value = pfx is null ? null : password;
        if (string.Equals(bootMediaCertificateHash, hash, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(bootMediaCertificatePassword, value, StringComparison.Ordinal))
        {
            return;
        }

        bootMediaCertificateHash = hash;
        bootMediaCertificatePassword = value;
        BootMediaCertificatePasswordChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ClearTenantConnection()
    {
        HasConnectedTenant = false;
        TenantOnboardingStatus = null;
        Certificates = [];
        SetBootMediaCertificatePassword(null, null);
    }
}
