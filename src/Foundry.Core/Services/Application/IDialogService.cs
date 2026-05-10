namespace Foundry.Core.Services.Application;

public interface IDialogService
{
    Task ShowMessageAsync(DialogRequest request);
    Task<bool> ConfirmAsync(ConfirmationDialogRequest request);
}
