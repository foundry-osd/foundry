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

    public string? GetBootMediaCertificatePassword(AuthoringFileReference? pfx) =>
        pfx is not null && string.Equals(bootMediaCertificateHash, pfx.ContentHash, StringComparison.OrdinalIgnoreCase)
            ? bootMediaCertificatePassword
            : null;

    public void SetBootMediaCertificatePassword(AuthoringFileReference? pfx, string? password)
    {
        bootMediaCertificateHash = pfx?.ContentHash;
        bootMediaCertificatePassword = pfx is null ? null : password;
    }

    public void ClearTenantConnection()
    {
        HasConnectedTenant = false;
        TenantOnboardingStatus = null;
        Certificates = [];
        SetBootMediaCertificatePassword(null, null);
    }
}
