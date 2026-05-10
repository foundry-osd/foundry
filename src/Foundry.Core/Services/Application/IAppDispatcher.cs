namespace Foundry.Core.Services.Application;

public interface IAppDispatcher
{
    bool HasThreadAccess { get; }
    bool TryEnqueue(Action action);
    Task EnqueueAsync(Action action);
}
