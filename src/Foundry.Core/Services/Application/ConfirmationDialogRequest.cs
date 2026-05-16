namespace Foundry.Core.Services.Application;

public sealed record ConfirmationDialogRequest(
    string Title,
    string Message,
    string PrimaryButtonText,
    string CancelButtonText,
    bool IsPrimaryButtonAccent = false);
