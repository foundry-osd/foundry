namespace Foundry.Deploy.Services.Deployment.PreOobe;

public enum PreOobeScriptPriority
{
    DriverProvisioning = 100,
    Customization = 300,
    Validation = 800,
    Cleanup = 900
}
