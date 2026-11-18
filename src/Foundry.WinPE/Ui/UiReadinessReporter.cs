// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Runtime;
using Foundry.Core.Services.Runtime;

namespace Foundry.WinPE.Ui;

/// <summary>Reports the UI process startup stages to the guardian. Never throws.</summary>
internal interface IUiReadinessReporter
{
    /// <summary>Reports <c>configuration_loaded</c>: Foundry Connect loaded its configuration and built its services.</summary>
    void ReportConfigurationLoaded();

    /// <summary>Reports <c>ui_ready</c>: the first content of the session (a phase page or the password prompt shown before it) rendered.</summary>
    void ReportUiReady();

    /// <summary>Reports <c>startup_failed</c> for a session that ended with <paramref name="exitCode"/> before any content rendered.</summary>
    void ReportStartupFailed(int exitCode);
}

/// <summary>
/// The readiness protocol v1 reporter of the UI process, under the application id <see cref="ApplicationId"/>. It is a
/// no-op when the process was not started by the guardian (no launch identity in the environment).
/// </summary>
internal sealed class UiReadinessReporter : IUiReadinessReporter
{
    /// <summary>The application id the UI process reports and the guardian expects.</summary>
    internal const string ApplicationId = "Foundry.WinPE";

    private readonly RuntimeStartupReporter? _reporter;

    private UiReadinessReporter(RuntimeStartupReporter? reporter)
    {
        _reporter = reporter;
    }

    /// <summary>Gets the directory of this launch's startup status file, or <see langword="null"/> without a guardian.</summary>
    internal string? StatusDirectory => _reporter is null ? null : Path.GetDirectoryName(_reporter.StatusPath);

    /// <summary>Reads the launch identity from the environment and reports <c>managed_started</c>.</summary>
    internal static UiReadinessReporter Start()
    {
        RuntimeStartupReporter? reporter = RuntimeStartupReporter.FromEnvironment(ApplicationId);
        reporter?.Report(StartupStage.ManagedStarted);
        return new UiReadinessReporter(reporter);
    }

    /// <inheritdoc />
    public void ReportConfigurationLoaded() => _reporter?.Report(StartupStage.ConfigurationLoaded);

    /// <inheritdoc />
    public void ReportUiReady() => _reporter?.Report(StartupStage.UiReady);

    /// <inheritdoc />
    public void ReportStartupFailed(int exitCode) => _reporter?.Report(StartupStage.StartupFailed, exitCode switch
    {
        (int)ChildRuntimeExitCode.ConfigurationFailure => "configuration",
        (int)ChildRuntimeExitCode.RuntimeFailure => "runtime",
        _ => "startup"
    });
}
