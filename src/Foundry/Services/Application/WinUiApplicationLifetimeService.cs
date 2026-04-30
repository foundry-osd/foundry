using Foundry.Core.Services.Application;

namespace Foundry.Services.Application;

public sealed class WinUiApplicationLifetimeService : IApplicationLifetimeService
{
    public void Shutdown()
    {
        Microsoft.UI.Xaml.Application.Current.Exit();
    }
}
