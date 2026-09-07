// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Diagnostics;
using System.Text;
using Foundry.Core.Services.WinPe;
using Foundry.Core.Tests.TestUtilities;

namespace Foundry.Core.Tests.WinPe;

public sealed class FoundryBootstrapBehaviorTests
{
    [Theory]
    [InlineData("success")]
    [InlineData("promotion")]
    [InlineData("rollback")]
    [InlineData("collision")]
    public async Task PromoteStagedCache_PreservesWorkingRuntimeAndRecoveryDirectories(string failure)
    {
        using var workspace = new TemporaryDirectory();
        string source = WinPeEmbeddedAssetService.ReadEmbeddedText("Foundry.Core.WinPe.FoundryBootstrap");
        string script = $$"""
            $ErrorActionPreference = 'Stop'
            $root = '{{workspace.Path.Replace("'", "''")}}'
            $active = [IO.Path]::Combine($root, 'runtime')
            $stage = [IO.Path]::Combine($root, 'staged')
            [IO.Directory]::CreateDirectory($active) | Out-Null
            [IO.Directory]::CreateDirectory($stage) | Out-Null
            [IO.File]::WriteAllText([IO.Path]::Combine($active, 'old.exe'), 'known-good')
            [IO.File]::WriteAllText([IO.Path]::Combine($stage, 'new.exe'), 'complete-new')
            $legacy = "$active.previous"
            [IO.Directory]::CreateDirectory($legacy) | Out-Null
            [IO.File]::WriteAllText([IO.Path]::Combine($legacy, 'retained.exe'), 'prior-recovery')
            $source = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('{{Convert.ToBase64String(Encoding.UTF8.GetBytes(source))}}'))
            $tokens = $null; $errors = $null
            $ast = [Management.Automation.Language.Parser]::ParseInput($source, [ref]$tokens, [ref]$errors)
            if ($errors.Count -ne 0) { throw 'Bootstrap did not parse under Windows PowerShell.' }
            $definition = @($ast.FindAll({ param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Promote-StagedCache' }, $true))
            if ($definition.Count -ne 1) { throw 'Expected one promotion function.' }
            $body = $definition[0].Extent.Text
            $script:moves = 0
            function Invoke-TestMove($from, $to) {
                $script:moves++
                if ($script:moves -eq 2 -and '{{failure}}' -ne 'success') {
                    if ('{{failure}}' -eq 'collision') { [IO.Directory]::CreateDirectory($to) | Out-Null }
                    throw 'Injected promotion failure.'
                }
                if ($script:moves -eq 3 -and '{{failure}}' -eq 'rollback') { throw 'Injected rollback failure.' }
                [IO.Directory]::Move($from, $to)
            }
            $body = $body.Replace('[System.IO.Directory]::Move($RuntimeCacheRoot, $backupRoot)', 'Invoke-TestMove -from $RuntimeCacheRoot -to $backupRoot')
            $body = $body.Replace('[System.IO.Directory]::Move($StagingRoot, $RuntimeCacheRoot)', 'Invoke-TestMove -from $StagingRoot -to $RuntimeCacheRoot')
            $body = $body.Replace('[System.IO.Directory]::Move($backupRoot, $RuntimeCacheRoot)', 'Invoke-TestMove -from $backupRoot -to $RuntimeCacheRoot')
            . ([scriptblock]::Create($body))
            $failed = $false
            $recovery = $false
            try { Promote-StagedCache -StagingRoot $stage -RuntimeCacheRoot $active }
            catch { $failed = $true; $recovery = $_.Exception.Data['PublicationRecoveryRequired'] -eq $true }
            if ([IO.File]::ReadAllText([IO.Path]::Combine($legacy, 'retained.exe')) -ne 'prior-recovery') { throw 'Lost prior recovery.' }
            if ('{{failure}}' -eq 'success') {
                if ($failed -or [IO.File]::ReadAllText([IO.Path]::Combine($active, 'new.exe')) -ne 'complete-new') { throw 'Publication failed.' }
            } elseif ('{{failure}}' -eq 'promotion') {
                if (-not $failed -or $recovery -or [IO.File]::ReadAllText([IO.Path]::Combine($active, 'old.exe')) -ne 'known-good') { throw 'Rollback failed.' }
            } else {
                $backups = @([IO.Directory]::GetDirectories($root, 'runtime.previous-*'))
                if (-not $failed -or -not $recovery -or $backups.Count -ne 1) { throw 'Recovery was not retained.' }
                if ([IO.File]::ReadAllText([IO.Path]::Combine($backups[0], 'old.exe')) -ne 'known-good') { throw 'Lost previous runtime.' }
                if ([IO.File]::ReadAllText([IO.Path]::Combine($stage, 'new.exe')) -ne 'complete-new') { throw 'Lost staged runtime.' }
            }
            [Console]::WriteLine('passed')
            """;
        string scriptPath = Path.Combine(workspace.Path, "bootstrap-test.ps1");
        File.WriteAllText(scriptPath, script, new UTF8Encoding(true));
        var start = new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                "System32", "WindowsPowerShell", "v1.0", "powershell.exe"),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (string argument in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", scriptPath })
            start.ArgumentList.Add(argument);
        using Process process = Process.Start(start)!;
        Task<string> stdout = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        Task<string> stderr = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            await process.WaitForExitAsync(deadline.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None);
            throw;
        }
        Assert.True(process.ExitCode == 0, await stderr);
        Assert.Equal("passed", (await stdout).Trim());
    }
}
