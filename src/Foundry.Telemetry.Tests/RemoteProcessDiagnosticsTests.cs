// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Utilities.Processes;

namespace Foundry.Telemetry.Tests;

public sealed class RemoteProcessDiagnosticsTests
{
    [Theory]
    [InlineData("Erreur\u00a0: 2")]
    [InlineData("Error: 2")]
    public void CreateProperties_PreservesDriverFailureFromMixedLanguageOutput(string errorHeader)
    {
        var result = new ProcessExecutionResult
        {
            FileName = "dism.exe",
            Arguments = "/Add-Driver /Driver:C:\\private\\drivers /Recurse",
            ExitCode = 2,
            StandardOutput = "Searching for driver packages to install...\r\n" +
                "There was a problem opening the INF file. C:\\Users\\Example User\\drivers\\invalid.inf Error: 0xE0000100.\r\n\r\n" +
                errorHeader + "\r\n\r\nNo driver packages were found on the specified path.\r\n" +
                "Verify that the driver .INF files are in the specified location and try the command again.\r\n"
        };

        IReadOnlyDictionary<string, object> properties = RemoteProcessDiagnostics.CreateProperties(result, TimeSpan.Zero);

        string output = Assert.IsType<string>(properties["ProcessStdout"]);
        Assert.Equal(false, properties["ProcessOutputOmitted"]);
        Assert.Contains("There was a problem opening the INF file.", output);
        Assert.Contains("0xE0000100", output);
        Assert.Contains("No driver packages were found on the specified path.", output);
        Assert.DoesNotContain("Example User", output);
        Assert.DoesNotContain("invalid.inf", output);
    }

    [Fact]
    public void CreateProperties_PreservesDismFailureWithoutCommandArguments()
    {
        var result = new ProcessExecutionResult
        {
            FileName = @"X:\Windows\System32\dism.exe",
            Arguments = "/English /Get-ImageInfo /ImageFile:\"E:\\private\\image.wim\" /Password:secret",
            WorkingDirectory = @"E:\private",
            ExitCode = 21,
            StandardOutput = "DISM\nError: 21\nThe device is not ready.",
            StandardError = "Password=secret"
        };

        IReadOnlyDictionary<string, object> properties = RemoteProcessDiagnostics.CreateProperties(result, TimeSpan.FromSeconds(2));

        Assert.Equal("dism.exe", properties["ToolName"]);
        Assert.Equal("Get-ImageInfo", properties["ProcessOperation"]);
        Assert.Equal(21, properties["ExitCode"]);
        Assert.Equal(2000d, properties["ProcessDurationMs"]);
        Assert.Contains("The device is not ready", properties["ProcessStdout"].ToString());
        string json = System.Text.Json.JsonSerializer.Serialize(properties);
        Assert.DoesNotContain("private", json);
        Assert.DoesNotContain("secret", json);
        Assert.DoesNotContain("ImageFile", json);
    }

    [Theory]
    [InlineData("powershell.exe")]
    [InlineData("cmd.exe")]
    [InlineData("netsh.exe")]
    [InlineData("reg.exe")]
    [InlineData("custom.exe")]
    public void CreateProperties_OmitsUnreviewedToolOutput(string tool)
    {
        var result = new ProcessExecutionResult
        {
            FileName = tool,
            Arguments = "secret",
            ExitCode = 1,
            StandardOutput = "unlabeled-sensitive-payload",
            StandardError = "also-sensitive"
        };

        IReadOnlyDictionary<string, object> properties = RemoteProcessDiagnostics.CreateProperties(result, TimeSpan.Zero);

        Assert.Equal(true, properties["ProcessOutputOmitted"]);
        Assert.False(properties.ContainsKey("ProcessStdout"));
        Assert.False(properties.ContainsKey("ProcessStderr"));
        Assert.DoesNotContain("secret", System.Text.Json.JsonSerializer.Serialize(properties));
    }

    [Fact]
    public void CreateProperties_DoesNotMistakeQuotedArgumentForOperation()
    {
        var result = new ProcessExecutionResult
        {
            FileName = "dism.exe",
            Arguments = "/ImageFile:\"E:\\folder /Get-ImageInfo\" /Apply-Image",
            ExitCode = 2
        };

        IReadOnlyDictionary<string, object> properties = RemoteProcessDiagnostics.CreateProperties(result, TimeSpan.Zero);

        Assert.Equal("Apply-Image", properties["ProcessOperation"]);
    }

    [Theory]
    [InlineData("diskpart.exe", "On computer: CORP-PC-123\nDiskPart has encountered an error: The device is not ready.\nSee the System Event Log for more information.", "The device is not ready")]
    [InlineData("7za.exe", "ERROR: Data Error : users\\Jane Doe\\private.docx", "Data Error")]
    [InlineData("dism.exe", "Name : CORP-PC-123\nDescription : users\\Jane Doe\\private.docx\nError: 21\nThe device is not ready.", "The device is not ready")]
    [InlineData("dism.exe", "Name : CORP-PC-123\nError: 21\n\nThe device is not ready.\n", "The device is not ready")]
    public void CreateProperties_ExportsDiagnosticCategoriesWithoutInventories(string tool, string output, string expected)
    {
        var result = new ProcessExecutionResult { FileName = tool, StandardOutput = output, ExitCode = 1 };

        IReadOnlyDictionary<string, object> properties = RemoteProcessDiagnostics.CreateProperties(result, TimeSpan.Zero);

        string exported = properties["ProcessStdout"].ToString()!;
        Assert.Contains(expected, exported);
        Assert.DoesNotContain("CORP-PC", exported);
        Assert.DoesNotContain("Jane", exported);
        Assert.DoesNotContain("private.docx", exported);
    }
}
