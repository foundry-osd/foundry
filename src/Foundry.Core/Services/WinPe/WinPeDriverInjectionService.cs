// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Services.WinPe;

public sealed class WinPeDriverInjectionService : IWinPeDriverInjectionService
{
    private readonly IWinPeProcessRunner _processRunner;

    public WinPeDriverInjectionService()
        : this(new WinPeProcessRunner())
    {
    }

    internal WinPeDriverInjectionService(IWinPeProcessRunner processRunner)
    {
        _processRunner = processRunner;
    }

    public async Task<WinPeResult> InjectAsync(
        WinPeDriverInjectionOptions options,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        WinPeDiagnostic? validationError = ValidateInjectionOptions(options);
        if (validationError is not null)
        {
            return WinPeResult.Failure(validationError);
        }

        string dismPath = ResolveDismPath(options.DismExecutablePath);
        string recurse = options.RecurseSubdirectories ? " /Recurse" : string.Empty;

        foreach (string packagePath in options.DriverPackagePaths)
        {
            string normalizedPath = packagePath.Trim();
            WinPeProcessExecution result = await WinPeDismProcessRunner.RunAsync(
                _processRunner,
                dismPath,
                $"/Image:{WinPeProcessRunner.Quote(options.MountedImagePath)} /Add-Driver /Driver:{WinPeProcessRunner.Quote(normalizedPath)}{recurse}",
                options.WorkingDirectoryPath,
                $"Injecting drivers from '{normalizedPath}'.",
                options.DismProgress,
                cancellationToken).ConfigureAwait(false);

            if (IsAcceptableDriverExitCode(result.ExitCode))
            {
                continue;
            }

            if (options.ContinueOnError)
            {
                // Skip this package but keep going; surface the failure as a non-fatal progress warning.
                options.DismProgress?.Report(new WinPeDismProgress
                {
                    Status = $"Skipped driver package '{normalizedPath}' (DISM exit code {result.ExitCode})."
                });
                continue;
            }

            return WinPeResult.Failure(
                WinPeErrorCodes.DriverInjectionFailed,
                "Failed to inject driver package into the mounted image.",
                result.ToDiagnosticText());
        }

        return WinPeResult.Success();
    }

    // DISM /Add-Driver exit codes treated as success. 0 = success; 50 is returned when a recursive folder
    // contains drivers DISM does not apply to WinPE (applicable drivers are still injected); 3010 = benign
    // "reboot required".
    private static bool IsAcceptableDriverExitCode(int exitCode)
    {
        return exitCode is 0 or 50 or 3010;
    }

    private static WinPeDiagnostic? ValidateInjectionOptions(WinPeDriverInjectionOptions? options)
    {
        if (options is null)
        {
            return new WinPeDiagnostic(
                WinPeErrorCodes.ValidationFailed,
                "Driver injection options are required.",
                "Provide a non-null WinPeDriverInjectionOptions instance.");
        }

        if (string.IsNullOrWhiteSpace(options.MountedImagePath) || !Directory.Exists(options.MountedImagePath))
        {
            return new WinPeDiagnostic(
                WinPeErrorCodes.ValidationFailed,
                "Mounted image path does not exist.",
                "Set WinPeDriverInjectionOptions.MountedImagePath to a mounted WinPE image.");
        }

        if (string.IsNullOrWhiteSpace(options.WorkingDirectoryPath) || !Directory.Exists(options.WorkingDirectoryPath))
        {
            return new WinPeDiagnostic(
                WinPeErrorCodes.ValidationFailed,
                "Driver injection working directory does not exist.",
                "Set WinPeDriverInjectionOptions.WorkingDirectoryPath to an existing folder.");
        }

        if (options.DriverPackagePaths.Count == 0)
        {
            return new WinPeDiagnostic(
                WinPeErrorCodes.ValidationFailed,
                "At least one driver package path is required.");
        }

        for (int index = 0; index < options.DriverPackagePaths.Count; index++)
        {
            string? path = options.DriverPackagePaths[index];
            if (string.IsNullOrWhiteSpace(path))
            {
                return new WinPeDiagnostic(
                    WinPeErrorCodes.ValidationFailed,
                    "Driver package path contains an empty value.",
                    $"Index: {index}.");
            }

            string normalizedPath = path.Trim();
            if (!Directory.Exists(normalizedPath) && !File.Exists(normalizedPath))
            {
                return new WinPeDiagnostic(
                    WinPeErrorCodes.ValidationFailed,
                    "Driver package path does not exist.",
                    $"Path: '{normalizedPath}'.");
            }
        }

        return null;
    }

    private static string ResolveDismPath(string? configuredPath)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            return configuredPath;
        }

        string windowsDirectory = Environment.GetEnvironmentVariable("WINDIR") ?? "C:\\Windows";
        return Path.Combine(windowsDirectory, "System32", "dism.exe");
    }
}
