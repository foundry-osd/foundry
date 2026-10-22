// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Services.WinPe;

/// <summary>
/// Applies the Foundry desktop background to a mounted boot image by copying the bundled image into
/// <c>Windows\System32</c> and pointing the WinPE <c>CustomBackground</c> value at it in the offline SOFTWARE hive.
/// </summary>
/// <remarks>
/// Loading an offline hive requires elevation, which media creation already holds. The hive is always unloaded
/// before returning so the image can be committed or discarded.
/// </remarks>
internal static class WinPeBootBackgroundProvisioner
{
    internal const string BackgroundResourceName = "Foundry.Core.WinPe.Background";
    private const string BackgroundFileName = "winpe.jpg";
    private const string RuntimeBackgroundPath = @"X:\Windows\System32\winpe.jpg";
    private const string WinPeKeyRelativePath = @"Microsoft\Windows NT\CurrentVersion\WinPE";
    private const string Stage = "Apply boot image background";

    public static async Task<WinPeResult> ApplyAsync(
        IWinPeProcessRunner processRunner,
        string mountedImagePath,
        string workingDirectoryPath,
        CancellationToken cancellationToken)
    {
        string system32Path = Path.Combine(mountedImagePath, "Windows", "System32");
        try
        {
            await CopyBackgroundAsync(Path.Combine(system32Path, BackgroundFileName), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return WinPeResult.Failure(new WinPeDiagnostic(
                WinPeErrorCodes.BuildFailed,
                "Failed to copy the Foundry background into the mounted boot image.",
                ex.Message,
                stage: Stage,
                exception: ex));
        }

        string regPath = Path.Combine(Environment.GetEnvironmentVariable("WINDIR") ?? @"C:\Windows", "System32", "reg.exe");
        string hiveKey = $@"HKLM\FoundryWinPeSoftware_{Guid.NewGuid():N}";
        string hivePath = Path.Combine(system32Path, "config", "SOFTWARE");

        WinPeProcessExecution load = await processRunner.RunAsync(
            regPath,
            $"load {WinPeProcessRunner.Quote(hiveKey)} {WinPeProcessRunner.Quote(hivePath)}",
            workingDirectoryPath,
            cancellationToken).ConfigureAwait(false);
        if (!load.IsSuccess)
        {
            return ToFailure(load, "Failed to load the boot image SOFTWARE hive.");
        }

        WinPeProcessExecution add = await processRunner.RunAsync(
            regPath,
            $"add {WinPeProcessRunner.Quote($@"{hiveKey}\{WinPeKeyRelativePath}")} /v CustomBackground /t REG_EXPAND_SZ /d {WinPeProcessRunner.Quote(RuntimeBackgroundPath)} /f",
            workingDirectoryPath,
            cancellationToken).ConfigureAwait(false);

        WinPeProcessExecution unload = await processRunner.RunAsync(
            regPath,
            $"unload {WinPeProcessRunner.Quote(hiveKey)}",
            workingDirectoryPath,
            cancellationToken).ConfigureAwait(false);

        if (!add.IsSuccess)
        {
            return ToFailure(add, "Failed to set the WinPE custom background in the boot image registry.");
        }

        return unload.IsSuccess
            ? WinPeResult.Success()
            : ToFailure(unload, "Failed to unload the boot image SOFTWARE hive.");
    }

    private static async Task CopyBackgroundAsync(string destinationPath, CancellationToken cancellationToken)
    {
        await using Stream source = typeof(WinPeBootBackgroundProvisioner).Assembly.GetManifestResourceStream(BackgroundResourceName)
            ?? throw new InvalidOperationException($"Embedded WinPE asset resource '{BackgroundResourceName}' was not found.");

        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        await using FileStream destination = File.Create(destinationPath);
        await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
    }

    private static WinPeResult ToFailure(WinPeProcessExecution execution, string message)
    {
        return WinPeResult.Failure(execution.ToFailureDiagnostic(
            WinPeErrorCodes.BuildFailed,
            message,
            stage: Stage,
            toolName: "reg.exe"));
    }
}
