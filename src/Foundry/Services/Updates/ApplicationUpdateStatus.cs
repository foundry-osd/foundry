namespace Foundry.Services.Updates;

public enum ApplicationUpdateStatus
{
    Ready,
    SkippedInDebug,
    NotInstalled,
    Checking,
    NoUpdate,
    UpdateAvailable,
    Downloading,
    ReadyToRestart,
    Failed
}
