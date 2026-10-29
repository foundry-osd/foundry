// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Deploy.Services.Deployment;
using Foundry.Deploy.Services.Deployment.PreOobe;

namespace Foundry.Deploy.Tests;

public sealed class PreOobeCopyProgressTests
{
    [Fact]
    public void Completed_ReportsTheBytesOfEveryFinishedFileAgainstTheTotal()
    {
        var reports = new List<(double Percent, string Detail)>();
        var progress = new PreOobeCopyProgress((percent, detail) => reports.Add((percent, detail)), 400);

        progress.Completed("Launch.cmd", 100);
        progress.Completed("setup.exe", 300);

        Assert.Equal([25d, 100d], reports.Select(report => report.Percent));
        Assert.Equal(PreOobeCopyProgress.FormatDetail("setup.exe", 400, 400), reports[^1].Detail);
    }

    [Fact]
    public void FormatDetail_NamesTheFileThePercentAndTheBytes()
    {
        string detail = PreOobeCopyProgress.FormatDetail("setup.exe", 50, 200);

        Assert.Equal($"setup.exe: 25% ({DeploymentStepExecutionContext.FormatByteSize(50)} / {DeploymentStepExecutionContext.FormatByteSize(200)})", detail);
    }
}
