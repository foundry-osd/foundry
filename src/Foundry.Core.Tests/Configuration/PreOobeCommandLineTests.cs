// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Configuration;

namespace Foundry.Core.Tests.Configuration;

public sealed class PreOobeCommandLineTests
{
    [Theory]
    [InlineData(false, null, "/i \"C:\\Content files\\setup.msi\"")]
    [InlineData(false, "/qn REBOOT=Force", "/i \"C:\\Content files\\setup.msi\" /qn REBOOT=Force")]
    [InlineData(true, "/quiet", "/i \"C:\\Content files\\setup.msi\" /quiet /l*v \"C:\\Logs\\action-1\\setup.log\"")]
    public void MsiUsesOnlyUserArgumentsAndOptInNamedLogging(bool log, string? arguments, string expected)
    {
        var action = new PreOobeActionSettings
        {
            Kind = PreOobeActionKind.Application,
            ApplicationMode = PreOobeApplicationMode.Msi,
            Arguments = arguments,
            GenerateInstallationLog = log
        };
        Assert.Equal(expected, PreOobeCommandLine.BuildArguments(action, @"C:\Content files\setup.msi", @"C:\Logs\action-1"));
    }

    [Theory]
    [InlineData(null, null, "-File \"C:\\Scripts\\my script.ps1\"")]
    [InlineData("-NoProfile -ExecutionPolicy Bypass", "-Name \"Example value\"", "-NoProfile -ExecutionPolicy Bypass -File \"C:\\Scripts\\my script.ps1\" -Name \"Example value\"")]
    public void PowerShellKeepsHostArgumentsBeforeFileAndScriptArgumentsAfter(string? host, string? script, string expected)
    {
        var action = new PreOobeActionSettings { Kind = PreOobeActionKind.PowerShell, PowerShellArguments = host, Arguments = script };
        Assert.Equal(expected, PreOobeCommandLine.BuildArguments(action, @"C:\Scripts\my script.ps1", @"C:\Logs"));
    }

    [Theory]
    [InlineData("echo value & exit /b 7")]
    [InlineData("\"C:\\Program Files\\Example\\tool.exe\" /option \"two words\"")]
    public void CommandLinePreservesTheCompleteUserCommand(string command)
    {
        Assert.Equal("/c \"" + command + "\"", PreOobeCommandLine.BuildArguments(
            new() { Kind = PreOobeActionKind.Command, Command = command }, string.Empty, string.Empty));
    }

    [Fact]
    public void ExeArgumentsRemainUnchanged()
    {
        const string arguments = " /custom=\"two words\"  /log=vendor.txt ";
        Assert.Equal(arguments, PreOobeCommandLine.BuildArguments(new()
        { Kind = PreOobeActionKind.Application, ApplicationMode = PreOobeApplicationMode.Exe, Arguments = arguments }, "app.exe", "logs"));
    }
}
