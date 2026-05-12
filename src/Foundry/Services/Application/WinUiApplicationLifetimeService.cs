using Foundry.Core.Services.Application;

namespace Foundry.Services.Application;

/// <summary>
/// Bridges application lifetime requests to the WinUI application instance.
/// </summary>
public sealed class WinUiApplicationLifetimeService : IApplicationLifetimeService
{
    /// <inheritdoc />
    public void Shutdown()
    {
        Microsoft.UI.Xaml.Application.Current.Exit();
    }
}
