namespace Foundry.Core.Services.Application;

/// <summary>
/// Describes a platform-neutral confirmation dialog request.
/// </summary>
/// <param name="Title">Dialog title shown to the user.</param>
/// <param name="Message">Dialog body message.</param>
/// <param name="PrimaryButtonText">Text for the action that confirms the operation.</param>
/// <param name="CancelButtonText">Text for the action that dismisses the operation.</param>
/// <param name="IsPrimaryButtonAccent">Whether the primary action should use the platform accent style.</param>
public sealed record ConfirmationDialogRequest(
    string Title,
    string Message,
    string PrimaryButtonText,
    string CancelButtonText,
    bool IsPrimaryButtonAccent = false);
