namespace Foundry.Deploy.Services.Deployment.PreOobe;

public interface IPreOobeScriptProvisioningService
{
    PreOobeScriptProvisioningResult Provision(
        string targetWindowsPartitionRoot,
        IEnumerable<PreOobeScriptDefinition> scripts);
}
