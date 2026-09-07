// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO;
using System.Globalization;
using Foundry.Core.Models.Configuration;
using System.Text.RegularExpressions;
using Foundry.Deploy.Models;
using Foundry.Deploy.Services.Logging;
using Foundry.Deploy.Services.System;
using Foundry.Utilities.Processes;
using Microsoft.Extensions.Logging;

namespace Foundry.Deploy.Services.Deployment;

/// <summary>Inspects and services Windows images using the existing deployment command and progress contracts.</summary>
public sealed class WindowsImagingService : IWindowsImagingService
{
    private static readonly TimeSpan MetadataExecutionTimeout = TimeSpan.FromMinutes(2);
    private readonly IProcessRunner _processRunner;
    private readonly ILogger<WindowsImagingService> _logger;
    private readonly WindowsDeploymentCommandRunner _commands;

    public WindowsImagingService(IProcessRunner processRunner, ILogger<WindowsImagingService> logger)
    {
        _processRunner = processRunner;
        _logger = logger;
        _commands = new(processRunner, logger);
    }

    /// <inheritdoc />
    public async Task<int> ResolveImageIndexAsync(
        string imagePath,
        string requestedEdition,
        string workingDirectory,
        CancellationToken cancellationToken = default)
    {
        return (await ResolveImageMetadataAsync(imagePath, requestedEdition, workingDirectory, cancellationToken).ConfigureAwait(false)).Index;
    }

    public async Task<WindowsImageInfo> InspectImageAsync(string imagePath, OperatingSystemCatalogItem selection,
        string workingDirectory, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(selection);
        ImageIndexMetadata metadata = await ResolveImageMetadataAsync(imagePath, selection.Edition, workingDirectory, cancellationToken).ConfigureAwait(false);
        WindowsImageInfo image = WindowsImageInfoParser.Parse(metadata.Output, metadata.Index);
        if (!image.Architecture.Equals(WindowsImageInfoParser.NormalizeArchitecture(selection.Architecture), StringComparison.Ordinal) ||
            image.Version.Major != 10 || image.Version.Minor != 0 ||
            selection.BuildMajor <= 0 || selection.BuildUbr < 0 ||
            image.Version.Build != selection.BuildMajor || image.Version.Revision != selection.BuildUbr ||
            string.IsNullOrWhiteSpace(selection.LanguageCode) || !image.DefaultLanguage.Equals(selection.LanguageCode, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The Windows image identity does not match the selected catalog image.");
        return image;
    }

    private async Task<ImageIndexMetadata> ResolveImageMetadataAsync(string imagePath, string requestedEdition,
        string workingDirectory, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(imagePath))
        {
            throw new FileNotFoundException("Operating system image was not found.", imagePath);
        }

        _logger.LogInformation("Resolving OS image index. ImagePath={ImagePath}, RequestedEdition={RequestedEdition}", imagePath, requestedEdition);
        ProcessExecutionResult execution = await _processRunner
            .RunAsync(
                "dism.exe",
                ["/English", "/Get-ImageInfo", $"/ImageFile:{imagePath}"],
                workingDirectory,
                cancellationToken,
                MetadataExecutionTimeout)
            .ConfigureAwait(false);

        if (!execution.IsSuccess)
        {
            _logger.LogError("Failed to resolve OS image index for {ImagePath}. Diagnostic={Diagnostic}", imagePath, VolumePathDiagnostics.Redact(execution.ToDiagnosticText()));
            throw new DeploymentProcessException(
                $"Unable to resolve image index for '{imagePath}'.{Environment.NewLine}{VolumePathDiagnostics.Redact(execution.ToDiagnosticText())}",
                execution.ExitCode);
        }

        execution.EnsureCompleteOutput();
        IReadOnlyList<int> imageIndexes = ParseImageIndexes(execution.StandardOutput);
        if (imageIndexes.Count == 0)
        {
            throw new InvalidOperationException($"The operating system image does not expose any image indexes: '{imagePath}'.");
        }

        WindowsEditionDefinition? requestedDefinition = WindowsEditionCatalog.Find(requestedEdition);
        if (requestedDefinition is null)
        {
            throw new InvalidOperationException($"Windows edition '{requestedEdition}' is not supported.");
        }

        var imageMetadata = new List<ImageIndexMetadata>(imageIndexes.Count);
        foreach (int imageIndex in imageIndexes)
        {
            ProcessExecutionResult detailedExecution = await _processRunner
                .RunAsync(
                    "dism.exe",
                    ["/English", "/Get-ImageInfo", $"/ImageFile:{imagePath}", $"/Index:{imageIndex}"],
                    workingDirectory,
                    cancellationToken,
                    MetadataExecutionTimeout)
                .ConfigureAwait(false);

            if (!detailedExecution.IsSuccess)
            {
                _logger.LogError(
                    "Failed to inspect OS image index {ImageIndex} for {ImagePath}. Diagnostic={Diagnostic}",
                    imageIndex,
                    imagePath,
                    VolumePathDiagnostics.Redact(detailedExecution.ToDiagnosticText()));
                throw new DeploymentProcessException(
                    $"Unable to inspect image index {imageIndex} in '{imagePath}'.{Environment.NewLine}{VolumePathDiagnostics.Redact(detailedExecution.ToDiagnosticText())}",
                    detailedExecution.ExitCode);
            }

            detailedExecution.EnsureCompleteOutput();
            imageMetadata.Add(new ImageIndexMetadata(imageIndex, WindowsImageInfoParser.ReadEdition(detailedExecution.StandardOutput), detailedExecution.StandardOutput));
        }

        ImageIndexMetadata[] matches = imageMetadata
            .Where(item => item.EditionId.Equals(requestedDefinition.EditionId, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (matches.Length != 1)
        {
            string availableEditionIds = string.Join(
                ", ",
                imageMetadata.Select(item => $"{item.Index}: {item.EditionId}"));

            throw new InvalidOperationException(
                $"Expected exactly one '{requestedDefinition.EditionId}' image for Windows edition '{requestedDefinition.Name}' in '{imagePath}', " +
                $"but found {matches.Length}. Available edition IDs: {availableEditionIds}.");
        }

        int resolvedIndex = matches[0].Index;
        _logger.LogInformation("Resolved OS image index {ImageIndex} for ImagePath={ImagePath}", resolvedIndex, imagePath);
        return matches[0];
    }

    /// <inheritdoc />
    public async Task ApplyImageAsync(
        string imagePath,
        int imageIndex,
        string windowsPartitionRoot,
        string scratchDirectory,
        string workingDirectory,
        CancellationToken cancellationToken = default,
        IProgress<double>? progress = null)
    {
        _logger.LogInformation("Applying OS image. ImagePath={ImagePath}, Index={ImageIndex}, WindowsPartitionRoot={WindowsPartitionRoot}",
            imagePath,
            imageIndex,
            windowsPartitionRoot);
        Directory.CreateDirectory(scratchDirectory);

        string[] arguments =
        [
            "/Apply-Image",
            $"/ImageFile:{imagePath}",
            $"/Index:{imageIndex}",
            $"/ApplyDir:{windowsPartitionRoot}",
            "/CheckIntegrity",
            $"/ScratchDir:{scratchDirectory}"
        ];

        if (progress is null)
        {
            await _commands.RunRequiredProcessAsync(
                "dism.exe",
                arguments,
                workingDirectory,
                $"OS image apply failed for index {imageIndex}",
                cancellationToken).ConfigureAwait(false);
        }
        else
        {
            DismProgressReporter progressReporter = new(progress);
            await _commands.RunRequiredProcessAsync(
                "dism.exe",
                arguments,
                workingDirectory,
                $"OS image apply failed for index {imageIndex}",
                cancellationToken,
                progressReporter.HandleOutput,
                progressReporter.HandleOutput).ConfigureAwait(false);

            if (progressReporter.HasReportedProgress)
            {
                progress.Report(100d);
            }
        }

        _logger.LogInformation("OS image apply completed. ImagePath={ImagePath}, Index={ImageIndex}", imagePath, imageIndex);
    }

    /// <inheritdoc />
    public async Task<string?> GetAppliedWindowsEditionAsync(
        string windowsPartitionRoot,
        string workingDirectory,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(windowsPartitionRoot))
        {
            throw new ArgumentException("Windows partition root is required.", nameof(windowsPartitionRoot));
        }

        string[] arguments =
        [
            "/English",
            $"/Image:{windowsPartitionRoot}",
            "/Get-CurrentEdition"
        ];

        ProcessExecutionResult execution = await _commands.RunRequiredProcessAsync(
            "dism.exe",
            arguments,
            workingDirectory,
            "Failed to query the applied Windows edition",
            cancellationToken, MetadataExecutionTimeout).ConfigureAwait(false);

        execution.EnsureCompleteOutput();
        Match editionMatch = Regex.Match(
            execution.StandardOutput,
            @"Current\s+Edition\s*:\s*(.+)",
            RegexOptions.IgnoreCase);

        if (!editionMatch.Success)
        {
            _logger.LogWarning("Unable to parse the applied Windows edition from DISM output.");
            return null;
        }

        string edition = editionMatch.Groups[1].Value.Trim();
        if (edition.Length == 0)
        {
            return null;
        }

        _logger.LogInformation("Detected applied Windows edition. Edition={Edition}", edition);
        return edition;
    }

    /// <inheritdoc />
    public async Task ApplyOfflineDriversAsync(
        string windowsPartitionRoot,
        string driverRoot,
        string scratchDirectory,
        string workingDirectory,
        CancellationToken cancellationToken = default,
        IProgress<double>? progress = null)
    {
        _logger.LogInformation("Applying offline drivers. DriverRoot={DriverRoot}, WindowsPartitionRoot={WindowsPartitionRoot}",
            driverRoot,
            windowsPartitionRoot);
        Directory.CreateDirectory(scratchDirectory);

        if (progress is null)
        {
            await _commands.RunRequiredProcessAsync(
                "dism.exe",
                [
                    $"/Image:{windowsPartitionRoot}",
                    "/Add-Driver",
                    $"/Driver:{driverRoot}",
                    "/Recurse",
                    $"/ScratchDir:{scratchDirectory}"
                ],
                workingDirectory,
                $"Offline driver injection failed for '{driverRoot}'",
                cancellationToken).ConfigureAwait(false);
        }
        else
        {
            DismProgressReporter progressReporter = new(progress);
            await _commands.RunRequiredProcessAsync(
                "dism.exe",
                [
                    $"/Image:{windowsPartitionRoot}",
                    "/Add-Driver",
                    $"/Driver:{driverRoot}",
                    "/Recurse",
                    $"/ScratchDir:{scratchDirectory}"
                ],
                workingDirectory,
                $"Offline driver injection failed for '{driverRoot}'",
                cancellationToken,
                progressReporter.HandleOutput,
                progressReporter.HandleOutput).ConfigureAwait(false);

            if (progressReporter.HasReportedProgress)
            {
                progress.Report(100d);
            }
        }

        _logger.LogInformation("Offline driver injection completed. DriverRoot={DriverRoot}", driverRoot);
    }

    private static IReadOnlyList<int> ParseImageIndexes(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return [];
        }

        var indexes = new List<int>();
        foreach (Match match in Regex.Matches(output, @"^[\t ]*Index[\t ]*:[\t ]*([^\r\n]*)\r?$", RegexOptions.IgnoreCase | RegexOptions.Multiline))
        {
            if (!int.TryParse(match.Groups[1].Value.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int index) ||
                index < 1 || indexes.Contains(index))
                throw new InvalidDataException("The image index list is invalid or ambiguous.");
            indexes.Add(index);
        }
        return indexes;
    }

    private sealed record ImageIndexMetadata(int Index, string EditionId, string Output);
}
