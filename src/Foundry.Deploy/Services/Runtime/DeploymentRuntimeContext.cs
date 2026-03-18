using Foundry.Deploy.Models;

namespace Foundry.Deploy.Services.Runtime;

public sealed record DeploymentRuntimeContext(DeploymentMode Mode, string? UsbCacheRuntimeRoot);
