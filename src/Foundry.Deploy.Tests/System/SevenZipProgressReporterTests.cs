// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Deploy.Services.System;

namespace Foundry.Deploy.Tests.Progress;

public sealed class SevenZipProgressReporterTests
{
    [Fact]
    public void HandleOutput_ReportsOnlyIncreasingPercentageProgress()
    {
        var progress = new CollectingProgress<double>();
        var reporter = new SevenZipProgressReporter(progress);

        reporter.HandleOutput("1 of 4 files completed");
        reporter.HandleOutput("12.5%");
        reporter.HandleOutput("10%");
        reporter.HandleOutput("12.5%");

        Assert.Equal(12.5d, Assert.Single(progress.Reports));
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
