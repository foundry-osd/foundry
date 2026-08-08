// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Deploy.Services.Deployment;

namespace Foundry.Deploy.Tests.Deployment;

public sealed class DismProgressReporterTests
{
    [Fact]
    public void HandleOutput_ReportsIncreasingPercentageAndOrdinalProgress()
    {
        var progress = new CollectingProgress<double>();
        var reporter = new DismProgressReporter(progress);

        reporter.HandleOutput("1 of 4 operations completed");
        reporter.HandleOutput("50.5%");
        reporter.HandleOutput("40%");
        reporter.HandleOutput("50.5%");

        Assert.Equal([25d, 50.5d], progress.Reports);
        Assert.True(reporter.HasReportedProgress);
    }

    [Fact]
    public void HandleOutput_PrefersPercentageWhenBothFormatsArePresent()
    {
        var progress = new CollectingProgress<double>();
        var reporter = new DismProgressReporter(progress);

        reporter.HandleOutput("1 of 4 operations completed: 50%");

        Assert.Equal(50d, Assert.Single(progress.Reports));
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
