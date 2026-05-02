namespace Foundry.Services.Updates;

public sealed class ApplicationUpdateStateChangedEventArgs(ApplicationUpdateCheckResult? currentResult) : EventArgs
{
    public ApplicationUpdateCheckResult? CurrentResult { get; } = currentResult;
}
