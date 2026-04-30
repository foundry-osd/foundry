using Microsoft.Extensions.Hosting;

namespace Foundry.DependencyInjection;

public static class FoundryHost
{
    public static IHost Create()
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Services.AddFoundryApplicationServices();
        return builder.Build();
    }
}
