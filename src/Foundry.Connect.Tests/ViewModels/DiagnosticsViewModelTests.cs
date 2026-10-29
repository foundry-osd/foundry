// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Connect.Tests.Support;
using Foundry.Connect.ViewModels;
using Microsoft.Extensions.Logging;

namespace Foundry.Connect.Tests.ViewModels;

public sealed class DiagnosticsViewModelTests
{
    [Fact]
    public void OpenLogFileCommand_WhenLogFileIsUnavailable_LogsWarningWithoutThrowing()
    {
        var logger = new RecordingLogger<DiagnosticsViewModel>();
        using var viewModel = new DiagnosticsViewModel(TestLocalization.Create(), logger);

        viewModel.OpenLogFileCommand.Execute(null);

        RecordedLogEntry entry = Assert.Single(logger.Entries, item => item.Level == LogLevel.Warning);
        Assert.True(entry.Properties.ContainsKey("LogFilePath"));
    }
}
