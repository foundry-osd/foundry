using Serilog;

namespace Foundry.Services.Updates;

/// <summary>
/// Stores the latest update check result and broadcasts it to shell subscribers.
/// </summary>
internal sealed class ApplicationUpdateStateService(ILogger logger) : IApplicationUpdateStateService
{
    private readonly ILogger logger = logger.ForContext<ApplicationUpdateStateService>();

    /// <inheritdoc />
    public event EventHandler<ApplicationUpdateStateChangedEventArgs>? StateChanged;

    /// <inheritdoc />
    public ApplicationUpdateCheckResult? CurrentResult { get; private set; }

    /// <inheritdoc />
    public void Publish(ApplicationUpdateCheckResult result)
    {
        CurrentResult = result;
        logger.Debug("Application update state changed. Status={Status}, Version={Version}", result.Status, result.Version);
        StateChanged?.Invoke(this, new ApplicationUpdateStateChangedEventArgs(CurrentResult));
    }

}
