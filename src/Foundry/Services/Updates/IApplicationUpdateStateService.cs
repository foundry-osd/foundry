namespace Foundry.Services.Updates;

public interface IApplicationUpdateStateService
{
    event EventHandler<ApplicationUpdateStateChangedEventArgs>? StateChanged;
    ApplicationUpdateCheckResult? CurrentResult { get; }
    void Publish(ApplicationUpdateCheckResult result);
}
