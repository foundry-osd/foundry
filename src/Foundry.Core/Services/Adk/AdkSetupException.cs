// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Services.Adk;

/// <summary>Preserves a setup failure category and native diagnostics without parsing localized error messages.</summary>
public sealed class AdkSetupException : InvalidOperationException
{
    /// <summary>Creates a classified setup failure associated with its installer and diagnostic log.</summary>
    public AdkSetupException(string reason, string setupPath, string logPath, int? nativeErrorCode = null, int? exitCode = null, Exception? innerException = null)
        : base($"ADK setup failed. Reason={reason}; NativeErrorCode={nativeErrorCode}; ExitCode={exitCode}.", innerException)
    {
        Reason = reason;
        SetupPath = setupPath;
        LogPath = logPath;
        NativeErrorCode = nativeErrorCode;
        ExitCode = exitCode;
    }

    /// <summary>Gets the stable, non-localized failure category.</summary>
    public string Reason { get; }

    /// <summary>Gets the installer path for local diagnostics.</summary>
    public string SetupPath { get; }

    /// <summary>Gets the requested setup log path; launch failures may not produce a file.</summary>
    public string LogPath { get; }

    /// <summary>Gets the Windows launch error, when available.</summary>
    public int? NativeErrorCode { get; }

    /// <summary>Gets the setup exit code, when a process completed.</summary>
    public int? ExitCode { get; }
}
