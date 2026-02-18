namespace Foundry.Services.WinPe;

public sealed class WinPeDriverInjectionService : IWinPeDriverInjectionService
{
    private readonly WinPeProcessRunner _processRunner = new();

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

        string dismPath = string.IsNullOrWhiteSpace(options.DismExecutablePath)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "dism.exe")
            : options.DismExecutablePath;

        if (!File.Exists(dismPath))
        {
            return WinPeResult.Failure(
                WinPeErrorCodes.ToolNotFound,
                "DISM executable was not found.",
                $"Expected path: '{dismPath}'.");
        }

        foreach (string packagePath in options.DriverPackagePaths)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string normalizedPath = packagePath.Trim();
            string recurse = options.RecurseSubdirectories ? " /Recurse" : string.Empty;
            string args = $"/Image:{WinPeProcessRunner.Quote(options.MountedImagePath)} /Add-Driver /Driver:{WinPeProcessRunner.Quote(normalizedPath)}{recurse}";

            WinPeProcessExecution result = await _processRunner.RunAsync(
                dismPath,
                args,
                options.WorkingDirectoryPath,
                cancellationToken).ConfigureAwait(false);

            if (!result.IsSuccess)
            {
                return WinPeResult.Failure(
                    WinPeErrorCodes.DriverInjectionFailed,
                    "Failed to inject one or more drivers into the mounted WinPE image.",
                    result.ToDiagnosticText());
            }
        }

        return WinPeResult.Success();
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

        if (string.IsNullOrWhiteSpace(options.MountedImagePath))
        {
            return new WinPeDiagnostic(
                WinPeErrorCodes.ValidationFailed,
                "Mounted image path is required.",
                "Set WinPeDriverInjectionOptions.MountedImagePath to a mounted WinPE image.");
        }

        if (!Directory.Exists(options.MountedImagePath))
        {
            return new WinPeDiagnostic(
                WinPeErrorCodes.ValidationFailed,
                "Mounted image path does not exist.",
                $"Path: '{options.MountedImagePath}'.");
        }

        if (string.IsNullOrWhiteSpace(options.WorkingDirectoryPath))
        {
            return new WinPeDiagnostic(
                WinPeErrorCodes.ValidationFailed,
                "Working directory path is required.",
                "Set WinPeDriverInjectionOptions.WorkingDirectoryPath to an existing folder.");
        }

        if (!Directory.Exists(options.WorkingDirectoryPath))
        {
            return new WinPeDiagnostic(
                WinPeErrorCodes.ValidationFailed,
                "Working directory path does not exist.",
                $"Path: '{options.WorkingDirectoryPath}'.");
        }

        if (options.DriverPackagePaths.Count == 0)
        {
            return new WinPeDiagnostic(
                WinPeErrorCodes.ValidationFailed,
                "At least one driver package path is required.",
                "Add one or more .inf or driver package paths.");
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
}
