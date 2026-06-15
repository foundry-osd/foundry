using System.IO;
using Foundry.Deploy.Services.System;
using Microsoft.Extensions.Logging;

namespace Foundry.Deploy.Services.Runtime;

public sealed class RecoveryTargetDiskResolver : IRecoveryTargetDiskResolver
{
    private const string RecoveryPartitionGuid = "de94bba4-06d1-4d40-a16a-bfd50179d6ac";
    private const string RecoveryMarkerRelativePath = @"Recovery\WindowsRE\FoundryOsRecovery.json";
    private readonly IProcessRunner _processRunner;
    private readonly ILogger<RecoveryTargetDiskResolver> _logger;
    private readonly Func<string, bool> _fileExists;

    public RecoveryTargetDiskResolver(IProcessRunner processRunner, ILogger<RecoveryTargetDiskResolver> logger)
        : this(processRunner, logger, File.Exists)
    {
    }

    internal RecoveryTargetDiskResolver(
        IProcessRunner processRunner,
        ILogger<RecoveryTargetDiskResolver> logger,
        Func<string, bool> fileExists)
    {
        _processRunner = processRunner;
        _logger = logger;
        _fileExists = fileExists;
    }

    public async Task<int?> ResolveAsync(CancellationToken cancellationToken = default)
    {
        ProcessExecutionResult listExecution = await RunDiskPartScriptAsync(
            ["list disk"],
            cancellationToken).ConfigureAwait(false);

        if (!listExecution.IsSuccess || string.IsNullOrWhiteSpace(listExecution.StandardOutput))
        {
            _logger.LogWarning("Unable to resolve active OS Recovery target disk. ExitCode={ExitCode}", listExecution.ExitCode);
            return null;
        }

        var candidateDiskNumbers = new List<int>();
        HashSet<char> usedLetters = DriveInfo.GetDrives()
            .Select(drive => char.ToUpperInvariant(drive.Name[0]))
            .ToHashSet();

        foreach (DiskPartDisk disk in DiskPartOutputParser.ParseListDisk(listExecution.StandardOutput))
        {
            ProcessExecutionResult partitionExecution = await RunDiskPartScriptAsync(
                [
                    $"select disk {disk.Number}",
                    "list partition"
                ],
                cancellationToken).ConfigureAwait(false);

            if (!partitionExecution.IsSuccess || string.IsNullOrWhiteSpace(partitionExecution.StandardOutput))
            {
                continue;
            }

            foreach (DiskPartPartition partition in DiskPartOutputParser.ParseListPartition(partitionExecution.StandardOutput))
            {
                ProcessExecutionResult detailExecution = await RunDiskPartScriptAsync(
                    [
                        $"select disk {disk.Number}",
                        $"select partition {partition.Number}",
                        "detail partition"
                    ],
                    cancellationToken).ConfigureAwait(false);

                if (!detailExecution.IsSuccess || string.IsNullOrWhiteSpace(detailExecution.StandardOutput))
                {
                    continue;
                }

                if (!DiskPartOutputParser
                        .ParseDetailPartitionTypeGuid(detailExecution.StandardOutput)
                        .Equals(RecoveryPartitionGuid, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                char? existingLetter = DiskPartOutputParser.ParseDetailPartitionDriveLetter(detailExecution.StandardOutput);
                if (existingLetter.HasValue &&
                    _fileExists($@"{existingLetter.Value}:\{RecoveryMarkerRelativePath}"))
                {
                    candidateDiskNumbers.Add(disk.Number);
                    continue;
                }

                char? letter = GetAvailableTemporaryLetter(usedLetters);
                if (letter is null)
                {
                    _logger.LogWarning("No temporary drive letter is available to inspect OS Recovery partition markers.");
                    return null;
                }

                usedLetters.Add(letter.Value);
                string markerPath = $@"{letter.Value}:\{RecoveryMarkerRelativePath}";
                bool assigned = false;
                try
                {
                    ProcessExecutionResult assignExecution = await RunDiskPartScriptAsync(
                        [
                            $"select disk {disk.Number}",
                            $"select partition {partition.Number}",
                            $"assign letter={letter}"
                        ],
                        cancellationToken).ConfigureAwait(false);

                    assigned = assignExecution.IsSuccess;
                    if (assigned && _fileExists(markerPath))
                    {
                        candidateDiskNumbers.Add(disk.Number);
                    }
                }
                finally
                {
                    if (assigned)
                    {
                        await RunDiskPartScriptAsync(
                            [
                                $"select volume {letter}",
                                $"remove letter={letter} noerr"
                            ],
                            CancellationToken.None).ConfigureAwait(false);
                    }

                    usedLetters.Remove(letter.Value);
                }
            }
        }

        if (candidateDiskNumbers.Count != 1)
        {
            _logger.LogWarning(
                "OS Recovery target disk resolver found {CandidateCount} Foundry recovery partition marker(s).",
                candidateDiskNumbers.Count);
            return null;
        }

        return candidateDiskNumbers[0];
    }

    private async Task<ProcessExecutionResult> RunDiskPartScriptAsync(
        IReadOnlyList<string> scriptLines,
        CancellationToken cancellationToken)
    {
        string scriptPath = Path.Combine(Path.GetTempPath(), $"foundry-recovery-diskpart-{Guid.NewGuid():N}.txt");
        try
        {
            await File.WriteAllLinesAsync(scriptPath, scriptLines, cancellationToken).ConfigureAwait(false);
            return await _processRunner
                .RunAsync("diskpart.exe", $"/s \"{scriptPath}\"", Path.GetTempPath(), cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            TryDeleteFile(scriptPath);
        }
    }

    private static char? GetAvailableTemporaryLetter(HashSet<char> usedLetters)
    {
        for (char letter = 'Z'; letter >= 'D'; letter--)
        {
            if (!usedLetters.Contains(letter))
            {
                return letter;
            }
        }

        return null;
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }
}
