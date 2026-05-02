using Serilog;

namespace Foundry.Services.Updates;

internal sealed class ApplicationUpdateStateService(ILogger logger) : IApplicationUpdateStateService
{
    private readonly ILogger logger = logger.ForContext<ApplicationUpdateStateService>();

    public event EventHandler<ApplicationUpdateStateChangedEventArgs>? StateChanged;

    public ApplicationUpdateCheckResult? CurrentResult { get; private set; }

    public void Publish(ApplicationUpdateCheckResult result)
    {
        CurrentResult = result;
        logger.Debug("Application update state changed. Status={Status}, Version={Version}", result.Status, result.Version);
        StateChanged?.Invoke(this, new ApplicationUpdateStateChangedEventArgs(CurrentResult));
    }

}
