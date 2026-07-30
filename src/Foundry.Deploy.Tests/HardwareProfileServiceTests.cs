// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Deploy.Models;
using Foundry.Deploy.Services.Hardware;
using Foundry.Deploy.Services.System;
using Microsoft.Extensions.Logging.Abstractions;

namespace Foundry.Deploy.Tests;

public sealed class HardwareProfileServiceTests
{
    [Fact]
    public async Task GetCurrentAsync_WhenFirmwareSecurityDataIsAvailable_MapsDeploymentPrerequisites()
    {
        const string payload = """
            {
              "Manufacturer": "Contoso",
              "Model": "Model 1",
              "Product": "Product 1",
              "SerialNumber": "ABC123",
              "Architecture": "AMD64",
              "IsOnBattery": false,
              "IsTpmPresent": true,
              "TpmSpecVersion": "2.0, 0, 1.16",
              "IsTpmEnabled": true,
              "IsTpmActivated": true,
              "FirmwareMode": "Uefi",
              "SecureBootState": "Enabled",
              "SystemFirmwareHardwareId": "",
              "PnpDevices": []
            }
            """;
        var runner = new StaticProcessRunner(payload);
        var service = new HardwareProfileService(runner, NullLogger<HardwareProfileService>.Instance);

        HardwareProfile profile = await service.GetCurrentAsync(TestContext.Current.CancellationToken);

        Assert.True(profile.IsTpmPresent);
        Assert.Equal("2.0, 0, 1.16", profile.TpmSpecVersion);
        Assert.True(profile.IsTpmEnabled);
        Assert.True(profile.IsTpmActivated);
        Assert.Equal(FirmwareMode.Uefi, profile.FirmwareMode);
        Assert.Equal(SecureBootState.Enabled, profile.SecureBootState);
    }

    private sealed class StaticProcessRunner(string standardOutput) : IProcessRunner
    {
        public Task<ProcessExecutionResult> RunAsync(
            string fileName,
            string arguments,
            string workingDirectory,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new ProcessExecutionResult
            {
                ExitCode = 0,
                StandardOutput = standardOutput
            });
        }

        public Task<ProcessExecutionResult> RunAsync(
            string fileName,
            IEnumerable<string> arguments,
            string workingDirectory,
            CancellationToken cancellationToken = default)
        {
            return RunAsync(fileName, string.Join(" ", arguments), workingDirectory, cancellationToken);
        }

        public Task<ProcessExecutionResult> RunAsync(
            string fileName,
            IEnumerable<string> arguments,
            string workingDirectory,
            Action<string>? onOutputData,
            Action<string>? onErrorData,
            CancellationToken cancellationToken = default)
        {
            return RunAsync(fileName, arguments, workingDirectory, cancellationToken);
        }
    }
}
