namespace Foundry.Core.Services.WinPe;

public sealed class WinPeIsoMediaService : IWinPeIsoMediaService
{
    private readonly IWinPeProcessRunner _processRunner;

    public WinPeIsoMediaService()
        : this(new WinPeProcessRunner())
    {
    }

    internal WinPeIsoMediaService(IWinPeProcessRunner processRunner)
    {
        _processRunner = processRunner;
    }

    public async Task<WinPeResult> CreateAsync(
        WinPeIsoMediaOptions options,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        WinPeDiagnostic? validationError = ValidateOptions(options);
        if (validationError is not null)
        {
            return WinPeResult.Failure(validationError);
        }

        WinPeWorkspacePreparationResult preparedWorkspace = options.PreparedWorkspace!;
        string requestedOutputPath = options.OutputIsoPath.Trim();
        string? preparedOutputPath = null;

        try
        {
            EnsureOutputDirectoryExists(requestedOutputPath);
            preparedOutputPath = PrepareOutputPath(requestedOutputPath, options.IsoTempDirectoryPath);

            if (options.ForceOverwriteOutput && File.Exists(preparedOutputPath))
            {
                File.Delete(preparedOutputPath);
            }

            string arguments =
                $"/ISO /F {WinPeProcessRunner.Quote(preparedWorkspace.Artifact.WorkingDirectoryPath)} {WinPeProcessRunner.Quote(preparedOutputPath)}" +
                (preparedWorkspace.UseBootEx ? " /bootex" : string.Empty);

            WinPeProcessExecution execution = await _processRunner.RunCmdScriptAsync(
                preparedWorkspace.Tools.MakeWinPeMediaPath,
                arguments,
                preparedWorkspace.Artifact.WorkingDirectoryPath,
                cancellationToken).ConfigureAwait(false);

            if (!execution.IsSuccess || !File.Exists(preparedOutputPath))
            {
                return WinPeResult.Failure(
                    WinPeErrorCodes.IsoCreateFailed,
                    "Failed to create WinPE ISO media.",
                    execution.ToDiagnosticText());
            }

            FinalizeOutput(preparedOutputPath, requestedOutputPath);
            return WinPeResult.Success();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return WinPeResult.Failure(
                WinPeErrorCodes.IsoCreateFailed,
                "Unexpected failure while creating WinPE ISO media.",
                ex.Message);
        }
        finally
        {
            CleanupPreparedOutput(requestedOutputPath, preparedOutputPath);
        }
    }

    private static WinPeDiagnostic? ValidateOptions(WinPeIsoMediaOptions? options)
    {
        if (options is null)
        {
            return new WinPeDiagnostic(
                WinPeErrorCodes.ValidationFailed,
                "ISO media options are required.",
                "Provide a non-null WinPeIsoMediaOptions instance.");
        }

        if (options.PreparedWorkspace is null)
        {
            return new WinPeDiagnostic(
                WinPeErrorCodes.ValidationFailed,
                "Prepared WinPE workspace is required.",
                "Set WinPeIsoMediaOptions.PreparedWorkspace.");
        }

        if (string.IsNullOrWhiteSpace(options.PreparedWorkspace.Tools.MakeWinPeMediaPath))
        {
            return new WinPeDiagnostic(
                WinPeErrorCodes.ValidationFailed,
                "MakeWinPEMedia path is required.",
                "Set WinPeToolPaths.MakeWinPeMediaPath.");
        }

        if (string.IsNullOrWhiteSpace(options.PreparedWorkspace.Artifact.WorkingDirectoryPath) ||
            !Directory.Exists(options.PreparedWorkspace.Artifact.WorkingDirectoryPath))
        {
            return new WinPeDiagnostic(
                WinPeErrorCodes.ValidationFailed,
                "Prepared WinPE workspace directory was not found.",
                $"Path: '{options.PreparedWorkspace.Artifact.WorkingDirectoryPath}'.");
        }

        if (string.IsNullOrWhiteSpace(options.OutputIsoPath) ||
            !options.OutputIsoPath.EndsWith(".iso", StringComparison.OrdinalIgnoreCase))
        {
            return new WinPeDiagnostic(
                WinPeErrorCodes.ValidationFailed,
                "Output ISO path must end with .iso.",
                $"Path: '{options.OutputIsoPath}'.");
        }

        if (ContainsNonAscii(options.OutputIsoPath) && string.IsNullOrWhiteSpace(options.IsoTempDirectoryPath))
        {
            return new WinPeDiagnostic(
                WinPeErrorCodes.ValidationFailed,
                "ISO temporary directory path is required for non-ASCII output paths.",
                "Set WinPeIsoMediaOptions.IsoTempDirectoryPath.");
        }

        return null;
    }

    private static string PrepareOutputPath(string requestedOutputPath, string isoTempDirectoryPath)
    {
        if (!ContainsNonAscii(requestedOutputPath))
        {
            return requestedOutputPath;
        }

        Directory.CreateDirectory(isoTempDirectoryPath);
        string fileName = Path.GetFileName(requestedOutputPath);
        string safeFileName = string.IsNullOrWhiteSpace(fileName)
            ? $"foundry-winpe-{DateTime.UtcNow:yyyyMMddHHmmssfff}.iso"
            : ToAsciiSafeFileName(fileName);

        if (!safeFileName.EndsWith(".iso", StringComparison.OrdinalIgnoreCase))
        {
            safeFileName += ".iso";
        }

        return Path.Combine(isoTempDirectoryPath, safeFileName);
    }

    private static void FinalizeOutput(string preparedOutputPath, string requestedOutputPath)
    {
        if (string.Equals(preparedOutputPath, requestedOutputPath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        EnsureOutputDirectoryExists(requestedOutputPath);
        File.Copy(preparedOutputPath, requestedOutputPath, overwrite: true);
    }

    private static void CleanupPreparedOutput(string requestedOutputPath, string? preparedOutputPath)
    {
        if (string.IsNullOrWhiteSpace(preparedOutputPath) ||
            string.Equals(preparedOutputPath, requestedOutputPath, StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(preparedOutputPath))
        {
            return;
        }

        try
        {
            File.Delete(preparedOutputPath);
        }
        catch
        {
            // Best-effort cleanup.
        }
    }

    private static void EnsureOutputDirectoryExists(string outputIsoPath)
    {
        string? outputDirectory = Path.GetDirectoryName(outputIsoPath);
        if (!string.IsNullOrWhiteSpace(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }
    }

    private static bool ContainsNonAscii(string value)
    {
        return value.Any(character => character > 127);
    }

    private static string ToAsciiSafeFileName(string fileName)
    {
        string sanitized = WinPeFileSystemHelper.SanitizePathSegment(fileName);
        char[] chars = sanitized
            .Select(character => character > 127 ? '_' : character)
            .ToArray();

        return new string(chars);
    }
}
