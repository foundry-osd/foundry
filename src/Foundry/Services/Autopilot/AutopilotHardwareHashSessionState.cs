using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Autopilot;

namespace Foundry.Services.Autopilot;

internal sealed class AutopilotHardwareHashSessionState : IAutopilotHardwareHashSessionState
{
    public bool HasConnectedTenant { get; set; }

    public AutopilotTenantOnboardingStatus? TenantOnboardingStatus { get; set; }

    public AutopilotBootMediaCertificateSettings BootMediaCertificate { get; set; } = new();

    public IReadOnlyList<AutopilotGraphKeyCredential> Certificates { get; set; } = [];

    public void ClearTenantConnection()
    {
        HasConnectedTenant = false;
        TenantOnboardingStatus = null;
        Certificates = [];
        BootMediaCertificate = new AutopilotBootMediaCertificateSettings();
    }
}
