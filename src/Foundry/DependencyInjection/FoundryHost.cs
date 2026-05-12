using Microsoft.Extensions.Hosting;

namespace Foundry.DependencyInjection;

/// <summary>
/// Creates the dependency injection host used by the WinUI application.
/// </summary>
public static class FoundryHost
{
    /// <summary>
    /// Builds the application host and registers Foundry services.
    /// </summary>
    /// <returns>The configured host instance.</returns>
    public static IHost Create()
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Services.AddFoundryApplicationServices();
        return builder.Build();
    }
}
