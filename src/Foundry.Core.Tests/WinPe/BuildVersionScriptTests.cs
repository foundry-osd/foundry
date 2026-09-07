// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Diagnostics;
using System.Text;
using System.Xml.Linq;
using Foundry.Core.Tests.TestUtilities;

namespace Foundry.Core.Tests.WinPe;

public sealed class BuildVersionScriptTests
{
    private const string OriginalVersion = "26.1.2.3";
    private const string Properties = "<Project><PropertyGroup><Version>26.1.2.3</Version><AssemblyVersion>26.1.2.3</AssemblyVersion><FileVersion>26.1.2.3</FileVersion><InformationalVersion>26.1.2.3</InformationalVersion><LangVersion>preview</LangVersion><IncludeSourceRevisionInInformationalVersion>false</IncludeSourceRevisionInInformationalVersion></PropertyGroup></Project>";

    [Fact]
    public async Task Update_ChangesExactlyFourVersionProperties()
    {
        using var workspace = new TemporaryDirectory();
        string path = WriteProperties(workspace.Path, Properties);
        (int exitCode, string output) = await RunAsync(workspace.Path, "26.9.7.123");
        Assert.True(exitCode == 0, output);
        string actual = File.ReadAllText(path);
        Assert.Equal(Properties.Replace(OriginalVersion, "26.9.7.123", StringComparison.Ordinal), actual);
        XElement group = XDocument.Parse(actual).Root!.Element("PropertyGroup")!;
        foreach (string property in new[] { "Version", "AssemblyVersion", "FileVersion", "InformationalVersion" })
            Assert.Equal("26.9.7.123", group.Element(property)!.Value);
        Assert.Equal("preview", group.Element("LangVersion")!.Value);
        Assert.Equal("false", group.Element("IncludeSourceRevisionInInformationalVersion")!.Value);
    }

    [Theory]
    [InlineData("Version", false)]
    [InlineData("AssemblyVersion", false)]
    [InlineData("FileVersion", false)]
    [InlineData("InformationalVersion", false)]
    [InlineData("Version", true)]
    [InlineData("AssemblyVersion", true)]
    [InlineData("FileVersion", true)]
    [InlineData("InformationalVersion", true)]
    public async Task Update_RejectsMissingOrDuplicatePropertyWithoutWriting(string property, bool duplicate)
    {
        string element = $"<{property}>{OriginalVersion}</{property}>";
        string content = Properties.Replace(element, duplicate ? element + element : "", StringComparison.Ordinal);
        await AssertRejectedWithoutWritingAsync(content, "26.9.7.1");
    }

    [Theory]
    [InlineData("26.2.30.1")]
    [InlineData("25.2.29.1")]
    [InlineData("26.13.1.1")]
    [InlineData("26.09.7.1")]
    [InlineData("26.9.7.0")]
    [InlineData("26.9.7.65535")]
    [InlineData("26.9.7.9999999999999999999999999")]
    public async Task Update_RejectsInvalidVersionWithoutWriting(string version)
        => await AssertRejectedWithoutWritingAsync(Properties, version);

    private static async Task AssertRejectedWithoutWritingAsync(string content, string version)
    {
        using var workspace = new TemporaryDirectory();
        string path = WriteProperties(workspace.Path, content);
        byte[] before = File.ReadAllBytes(path);
        (int exitCode, _) = await RunAsync(workspace.Path, version);
        Assert.NotEqual(0, exitCode);
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    private static string WriteProperties(string root, string content)
    {
        string directory = Path.Combine(root, "src");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "Directory.Build.props");
        File.WriteAllText(path, content, new UTF8Encoding(true));
        return path;
    }

    private static async Task<(int ExitCode, string Output)> RunAsync(string root, string version)
    {
        DirectoryInfo? repo = new(AppContext.BaseDirectory);
        while (repo is not null && !File.Exists(Path.Combine(repo.FullName, "scripts", "Set-FoundryBuildVersion.ps1"))) repo = repo.Parent;
        Assert.NotNull(repo);
        var start = new ProcessStartInfo("pwsh") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string argument in new[] { "-NoProfile", "-NonInteractive", "-File", Path.Combine(repo.FullName, "scripts", "Set-FoundryBuildVersion.ps1"), "-RepositoryRoot", root, "-Version", version }) start.ArgumentList.Add(argument);
        using Process process = Process.Start(start)!;
        Task<string> stdout = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        Task<string> stderr = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(15));
        try { await process.WaitForExitAsync(deadline.Token); }
        catch (OperationCanceledException) { process.Kill(true); await process.WaitForExitAsync(CancellationToken.None); throw; }
        return (process.ExitCode, await stdout + await stderr);
    }
}
