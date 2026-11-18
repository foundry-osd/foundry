// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Runtime;
using Serilog;

namespace Foundry.WinPE.Ui;

/// <summary>
/// Classifies how the UI process ends, apart from WPF: a failure while it sets up is a startup failure, an exception that
/// leaves the running application is a crash.
/// </summary>
internal static class UiProcessExit
{
    /// <summary>
    /// Exit code of a UI process ended by an unhandled exception once its application runs. It is none of the codes the
    /// guardian reads as an outcome (0, 20, 21, 22, 23), so the guardian treats it as a crash.
    /// </summary>
    internal const int UnhandledExceptionExitCode = 30;

    /// <summary>
    /// Runs <paramref name="setUp"/>, then the application it returns, and returns the process exit code.
    /// </summary>
    /// <remarks>
    /// An exception from <paramref name="setUp"/> means no window can have rendered: it is logged, reported as
    /// <c>startup_failed</c> and ends the process with <see cref="ChildRuntimeExitCode.StartupFailure"/>. An exception from
    /// the running application (a dispatcher exception no handler marked handled leaves <c>Application.Run</c>) is logged and
    /// ends the process with <see cref="UnhandledExceptionExitCode"/>, without any readiness report: the guardian classifies
    /// it by the exit code.
    /// </remarks>
    /// <param name="setUp">Prepares the process and returns the call that runs its application.</param>
    /// <param name="readiness">Receives <c>startup_failed</c> for a setup failure.</param>
    /// <param name="logger">The host log.</param>
    internal static int Run(Func<Func<int>> setUp, IUiReadinessReporter readiness, ILogger logger)
    {
        Func<int> run;
        try
        {
            run = setUp();
        }
        catch (Exception exception)
        {
            logger.Fatal(exception, "Foundry UI process could not start.");
            readiness.ReportStartupFailed((int)ChildRuntimeExitCode.StartupFailure);
            return (int)ChildRuntimeExitCode.StartupFailure;
        }

        try
        {
            return run();
        }
        catch (Exception exception)
        {
            logger.Fatal(exception, "Unhandled exception in the running UI process. The UI process ends.");
            return UnhandledExceptionExitCode;
        }
    }
}
