namespace Foundry.Deploy.Services.Deployment;

public interface ISetupCompleteScriptService
{
    string EnsureBlock(string setupCompletePath, string markerKey, string scriptBody);

    string RemoveBlock(string setupCompletePath, string markerKey);
}
