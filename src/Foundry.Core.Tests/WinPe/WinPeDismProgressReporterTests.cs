using Foundry.Core.Services.WinPe;

namespace Foundry.Core.Tests.WinPe;

public sealed class WinPeDismProgressReporterTests
{
    [Fact]
    public void HandleOutput_ReportsPercentProgress()
    {
        var progress = new CollectingProgress<WinPeDismProgress>();
        var reporter = new WinPeDismProgressReporter("Applying DISM changes.", progress);

        reporter.HandleOutput("[==========================50.0%==========================]");

        WinPeDismProgress report = Assert.Single(progress.Reports);
        Assert.Equal(50, report.Percent);
        Assert.Equal("Applying DISM changes.", report.Status);
    }

    [Fact]
    public void HandleOutput_ReportsFrenchOrdinalProgress()
    {
        var progress = new CollectingProgress<WinPeDismProgress>();
        var reporter = new WinPeDismProgressReporter("Applying DISM changes.", progress);

        reporter.HandleOutput("3 sur 4 operations completed");

        WinPeDismProgress report = Assert.Single(progress.Reports);
        Assert.Equal(75, report.Percent);
    }

    [Fact]
    public void HandleOutput_IgnoresRegressingProgress()
    {
        var progress = new CollectingProgress<WinPeDismProgress>();
        var reporter = new WinPeDismProgressReporter("Applying DISM changes.", progress);

        reporter.HandleOutput("70%");
        reporter.HandleOutput("20%");

        WinPeDismProgress report = Assert.Single(progress.Reports);
        Assert.Equal(70, report.Percent);
    }

    private sealed class CollectingProgress<T> : IProgress<T>
    {
        public List<T> Reports { get; } = [];

        public void Report(T value)
        {
            Reports.Add(value);
        }
    }
}
