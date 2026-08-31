// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO;
using Foundry.Connect.Services.Runtime;
using Foundry.Utilities.IO;
using Foundry.Utilities.Diagnostics;
using Serilog;
using Serilog.Events;

namespace Foundry.Connect.Services.Logging;

internal static class FoundryConnectLogging
{
    public const string LogFileName = "FoundryConnect.log";

    private const int RetainedLogFileCount = 5;

    public static string CurrentLogFilePath { get; private set; } = "<unavailable>";

    public static string ResolveStartupLogFilePath()
    {
        return WritableFilePathResolver.Resolve(
            ConnectWorkspacePaths.EnumerateStartupLogDirectories(),
            LogFileName);
    }

    public static ILogger CreateLogger(string logFilePath)
    {
        string normalizedLogFilePath = Path.GetFullPath(logFilePath);
        ILogger logger = FoundryLogConfiguration.CreateFileLogger(
            logFilePath,
            "Foundry.Connect",
            DiagnosticSessionContext.CurrentSessionId,
            LogEventLevel.Debug,
            RetainedLogFileCount);

        CurrentLogFilePath = normalizedLogFilePath;
        return logger;
    }
}
