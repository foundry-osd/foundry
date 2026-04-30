namespace Foundry.Core.Services.Application;

public sealed record DialogRequest(
    string Title,
    string Message,
    string CloseButtonText = "OK");
