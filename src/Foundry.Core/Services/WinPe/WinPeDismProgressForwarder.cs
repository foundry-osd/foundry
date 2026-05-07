namespace Foundry.Core.Services.WinPe;

internal sealed class WinPeDismProgressForwarder : IProgress<WinPeDismProgress>
{
    private readonly IProgress<WinPeMountedImageCustomizationProgress> _progress;
    private readonly int _percent;
    private readonly string _status;

    public WinPeDismProgressForwarder(
        IProgress<WinPeMountedImageCustomizationProgress> progress,
        int percent,
        string status)
    {
        _progress = progress;
        _percent = percent;
        _status = status;
    }

    public void Report(WinPeDismProgress value)
    {
        _progress.Report(new WinPeMountedImageCustomizationProgress
        {
            Percent = _percent,
            Status = _status,
            DetailPercent = value.Percent,
            DetailStatus = value.Status
        });
    }
}
