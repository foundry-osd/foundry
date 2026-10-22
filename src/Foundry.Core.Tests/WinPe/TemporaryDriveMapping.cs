// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Tests.WinPe;

/// <summary>Maps a temporary folder to a free drive letter with subst.exe, so USB workflow tests can use drive-letter roots.</summary>
internal sealed class TemporaryDriveMapping : IDisposable
{
    private static readonly object SyncRoot = new();

    private TemporaryDriveMapping(string driveLetter)
    {
        DriveLetter = driveLetter;
        RootPath = $@"{driveLetter}\";
    }

    public string DriveLetter { get; }
    public string RootPath { get; }

    public static TemporaryDriveMapping Create(string workspaceRootPath, string name)
    {
        lock (SyncRoot)
        {
            string backingPath = Path.Combine(workspaceRootPath, name);
            Directory.CreateDirectory(backingPath);

            for (char drive = 'Z'; drive >= 'P'; drive--)
            {
                string driveLetter = $"{drive}:";
                if (Directory.Exists($@"{driveLetter}\"))
                {
                    continue;
                }

                RunSubst($"{driveLetter} \"{backingPath}\"");
                return new TemporaryDriveMapping(driveLetter);
            }
        }

        throw new InvalidOperationException("No free drive letter was available for USB media tests.");
    }

    public void Dispose()
    {
        RunSubst($"{DriveLetter} /D");
    }

    private static void RunSubst(string arguments)
    {
        using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = "subst.exe",
            Arguments = arguments,
            CreateNoWindow = true,
            UseShellExecute = false
        });

        process?.WaitForExit();
        if (process is null || process.ExitCode != 0)
        {
            throw new InvalidOperationException($"Failed to run subst.exe {arguments}.");
        }
    }
}
