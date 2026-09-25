// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Foundry.Deploy.Services.Autopilot;
using Foundry.Deploy.Services.Deployment;

namespace Foundry.Deploy.Tests;

public sealed class AutopilotRegistrationScriptTests
{
    [Theory]
    [InlineData("FINAL-PC", false, false)]
    [InlineData(null, false, false)]
    [InlineData("FINAL-PC", true, false)]
    [InlineData("FINAL-PC", true, true)]
    public async Task Readiness_AssignsRequestedPropertiesAndWaitsForReadback(string? name, bool existingDevice, bool caseOnlyChange)
    {
        using JsonDocument result = await RunReadinessAsync(name, existingDevice, "success", caseOnlyChange);
        JsonElement output = result.RootElement;
        Assert.Equal("Pending", output.GetProperty("firstStatus").GetString());
        Assert.Equal("Pending", output.GetProperty("waitingStatus").GetString());
        Assert.Equal("Completed", output.GetProperty("finalStatus").GetString());
        Assert.Equal(1, output.GetProperty("updates").GetInt32());
        JsonElement body = output.GetProperty("body");
        Assert.Equal("NEW", body.GetProperty("groupTag").GetString());
        if (name is null)
        {
            Assert.False(body.TryGetProperty("displayName", out _));
        }
        else
        {
            Assert.Equal(name, body.GetProperty("displayName").GetString());
        }
    }

    [Theory]
    [InlineData("timeout")]
    [InlineData("rejected")]
    public async Task Readiness_WhenAssignmentFails_ExplainsThatHardwareHashIsAlreadyVisible(string outcome)
    {
        using JsonDocument result = await RunReadinessAsync("FINAL-PC", false, outcome);
        string message = result.RootElement.GetProperty("error").GetString()!;
        Assert.Contains("visible", message);
        Assert.Contains("computer name", message);
    }

    private static async Task<JsonDocument> RunReadinessAsync(string? name, bool existingDevice, string outcome, bool caseOnlyChange = false)
    {
        string root = Path.Combine(Path.GetTempPath(), "FoundryAutopilotScriptTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var service = new AutopilotInteractiveRegistrationProvisioningService(new SetupCompleteScriptService());
            var staged = service.Provision(root, name);
            string script = $$"""
                $ErrorActionPreference = 'Stop'
                $tokens = $null
                $errors = $null
                $ast = [System.Management.Automation.Language.Parser]::ParseFile('{{staged.ScriptPath.Replace("'", "''")}}', [ref]$tokens, [ref]$errors)
                if ($errors.Count -gt 0) { throw ($errors | Out-String) }
                foreach ($function in $ast.EndBlock.Statements | Where-Object { $_ -is [System.Management.Automation.Language.FunctionDefinitionAst] }) {
                    . ([scriptblock]::Create($function.Extent.Text))
                }
                function Write-State { param($Stage, $Message) }
                function Write-FoundryLog { param($Path, $Message) }
                function Find-AutopilotDeviceBySerialNumber { param($AccessToken, $SerialNumber) return $script:Device }
                function Invoke-GraphRequest {
                    param($Method, $Path, $AccessToken, $Body)
                    $script:Body = $Body
                    $script:Updates++
                    if ('{{outcome}}' -eq 'rejected') { throw 'Graph rejected properties.' }
                }
                $script:Updates = 0
                $script:UploadDevicePropertiesUpdateRequested = $false
                $script:Device = [pscustomobject]@{ id = 'device'; serialNumber = 'SER123'; groupTag = '{{(caseOnlyChange ? "NEW" : "OLD")}}'; displayName = '{{(caseOnlyChange ? "final-pc" : "OLD-PC")}}' }
                $imported = [pscustomobject]@{ state = [pscustomobject]@{ deviceImportStatus = '{{(existingDevice ? "error" : "complete")}}'; deviceErrorName = 'ZtdDeviceAlreadyAssigned' } }
                $arguments = @{
                    AccessToken = 'token'
                    Identity = [pscustomobject]@{ SerialNumber = 'SER123' }
                    Import = [pscustomobject]@{ ImportId = 'import' }
                    ImportedIdentity = [ref]$imported
                    Deadline = [DateTimeOffset]::UtcNow.AddMinutes(1)
                    GroupTag = 'NEW'
                    AssignedComputerName = '{{name}}'
                }
                if ('{{outcome}}' -eq 'timeout') { $arguments.Deadline = [DateTimeOffset]::UtcNow.AddSeconds(-1) }
                try {
                    $first = Test-AutopilotDeviceReadiness @arguments
                    $waiting = Test-AutopilotDeviceReadiness @arguments
                    $script:Device.groupTag = 'NEW'
                    if (-not [string]::IsNullOrWhiteSpace($arguments.AssignedComputerName)) { $script:Device.displayName = $arguments.AssignedComputerName }
                    $final = Test-AutopilotDeviceReadiness @arguments
                    @{ firstStatus = $first.Status; waitingStatus = $waiting.Status; finalStatus = $final.Status; updates = $script:Updates; body = $script:Body } | ConvertTo-Json -Compress
                }
                catch { @{ error = $_.Exception.Message } | ConvertTo-Json -Compress }
                """;
            var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe"))
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            start.ArgumentList.Add("-NoProfile");
            start.ArgumentList.Add("-EncodedCommand");
            start.ArgumentList.Add(Convert.ToBase64String(Encoding.Unicode.GetBytes(script)));
            using Process process = Process.Start(start)!;
            Task<string> output = process.StandardOutput.ReadToEndAsync();
            Task<string> error = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);
                throw;
            }
            Assert.True(process.ExitCode == 0, await error);
            return JsonDocument.Parse(await output);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
