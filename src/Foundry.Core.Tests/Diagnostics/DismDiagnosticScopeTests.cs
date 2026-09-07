// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Services.Diagnostics;
using Foundry.Utilities.Processes;

namespace Foundry.Core.Tests.Diagnostics;

public sealed class DismDiagnosticScopeTests
{
    [Fact]
    public void CaptureUsesCurrentOwnedScopeAndExcludesOtherProcessOutput()
    {
        string root = Path.Combine(Path.GetTempPath(), "foundry-dism-evidence-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var scope = new DismDiagnosticScope(root);
            DismDiagnosticScope.Record("powershell.exe", new() { StandardOutput = "excluded-output" });
            Assert.Null(scope.CapturedLogPath);
            DismDiagnosticScope.Record("dism.exe", new() { ExitCode = 87, StandardError = "Error: 87" });
            Assert.Equal(scope.LogPath, scope.CapturedLogPath);
            string content = File.ReadAllText(scope.LogPath);
            Assert.Contains("DISM exit=87", content);
            Assert.DoesNotContain("excluded-output", content);
            using (var nested = new DismDiagnosticScope(Path.Combine(root, "nested")))
                DismDiagnosticScope.Record("dism.exe", new() { StandardOutput = "nested-output" });
            DismDiagnosticScope.Record("dism.exe", new() { StandardOutput = "outer-output" });
            Assert.DoesNotContain("nested-output", File.ReadAllText(scope.LogPath));
            Assert.Contains("outer-output", File.ReadAllText(scope.LogPath));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void NewOperationClearsPreviousEvidenceAndCaptureIsBounded()
    {
        string root = Path.Combine(Path.GetTempPath(), "foundry-dism-bound-" + Guid.NewGuid().ToString("N"));
        try
        {
            using (var prior = new DismDiagnosticScope(root))
                DismDiagnosticScope.Record("dism.exe", new() { StandardOutput = "previous-operation" });
            using var current = new DismDiagnosticScope(root);
            Assert.Empty(File.ReadAllText(current.LogPath));
            for (int i = 0; i < 12; i++)
                DismDiagnosticScope.Record("dism.exe", new() { StandardOutput = new string('x', 1024 * 1024) });
            Assert.InRange(new FileInfo(current.LogPath).Length, 1, 10 * 1024 * 1024);
            Assert.DoesNotContain("previous-operation", File.ReadAllText(current.LogPath));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void RedirectedDirectoryDoesNotOverwriteOtherOwnedEvidence()
    {
        string root = Path.Combine(Path.GetTempPath(), "foundry-dism-link-" + Guid.NewGuid().ToString("N"));
        string destination = Path.Combine(root, "destination");
        string link = Path.Combine(root, "link");
        try
        {
            Directory.CreateDirectory(destination);
            string protectedFile = Path.Combine(destination, "Foundry.Dism.log");
            File.WriteAllText(protectedFile, "preserve");
            Directory.CreateSymbolicLink(link, destination);
            using var scope = new DismDiagnosticScope(link);
            DismDiagnosticScope.Record("dism.exe", new() { StandardOutput = "new-output" });
            Assert.Null(scope.CapturedLogPath);
            Assert.Equal("preserve", File.ReadAllText(protectedFile));
        }
        finally
        {
            if (Directory.Exists(link)) Directory.Delete(link);
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
}
