namespace Foundry.Deploy.Services.Runtime;

public interface IRecoveryTargetDiskResolver
{
    Task<int?> ResolveAsync(CancellationToken cancellationToken = default);
}
