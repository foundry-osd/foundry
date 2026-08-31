// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO;
using Foundry.Utilities.IO;
using Foundry.Utilities.Diagnostics;
using Serilog;
using Serilog.Events;

namespace Foundry.Deploy.Services.Logging;

internal static class FoundryDeployLogging
{
    public const string LogFileName = "FoundryDeploy.log";

    private const int RetainedLogFileCount = 5;

    public static string ResolveStartupLogFilePath()
    {
        string[] candidateDirectories =
        [
            @"X:\Foundry\Logs",
            Path.Combine(Path.GetTempPath(), "Foundry", "Logs"),
            AppContext.BaseDirectory
        ];

        return WritableFilePathResolver.Resolve(candidateDirectories, LogFileName);
    }

    public static ILogger CreateLogger(string logFilePath)
    {
        return FoundryLogConfiguration.CreateFileLogger(
            logFilePath,
            "Foundry.Deploy",
            DiagnosticSessionContext.CurrentSessionId,
            LogEventLevel.Debug,
            RetainedLogFileCount);
    }
}

