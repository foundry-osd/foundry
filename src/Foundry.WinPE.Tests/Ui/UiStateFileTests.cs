// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Text.Json;
using Foundry.WinPE.Ui;
using Xunit;

namespace Foundry.WinPE.Tests.Ui;

public sealed class UiStateFileTests : IDisposable
{
    private readonly string directory = Directory.CreateTempSubdirectory("FoundryUiState-").FullName;

    [Theory]
    [InlineData((int)UiPhase.Connect, false, "connect")]
    [InlineData((int)UiPhase.System, false, "system")]
    [InlineData((int)UiPhase.Deploy, true, "deploy")]
    public void Write_ThenRead_RoundTripsTheDocumentedJson(int phaseValue, bool destructiveStarted, string phaseName)
    {
        var phase = (UiPhase)phaseValue;
        string path = Path.Combine(directory, "ui-state.json");

        UiStateFile.Write(path, new UiState(phase, destructiveStarted));

        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
        Assert.Equal(phaseName, document.RootElement.GetProperty("phase").GetString());
        Assert.Equal(destructiveStarted, document.RootElement.GetProperty("destructiveStarted").GetBoolean());
        Assert.Equal(new UiState(phase, destructiveStarted), UiStateFile.Read(path));
    }

    [Fact]
    public void Write_ReplacesThePreviousStateWithoutLeavingATemporaryFile()
    {
        string path = Path.Combine(directory, "ui-state.json");

        UiStateFile.Write(path, new UiState(UiPhase.Connect, DestructiveStarted: false));
        UiStateFile.Write(path, new UiState(UiPhase.Deploy, DestructiveStarted: true));

        Assert.Equal(new UiState(UiPhase.Deploy, DestructiveStarted: true), UiStateFile.Read(path));
        Assert.Equal([path], Directory.GetFiles(directory));
    }

    [Fact]
    public void Read_MissingFile_IsUnknown()
    {
        // The session records its state before any window, so a missing file means a failed write: never assumed safe.
        Assert.Null(UiStateFile.Read(Path.Combine(directory, "missing.json")));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"phase\":\"unknown\",\"destructiveStarted\":false}")]
    [InlineData("{\"phase\":\"deploy\"}")]
    public void Read_UnreadableState_IsUnknown(string content)
    {
        string path = Path.Combine(directory, "ui-state.json");
        File.WriteAllText(path, content);

        Assert.Null(UiStateFile.Read(path));
    }

    public void Dispose() => Directory.Delete(directory, recursive: true);
}
