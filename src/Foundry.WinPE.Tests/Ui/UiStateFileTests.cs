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
    public void Write_ThenRead_RoundTripsTheCulture()
    {
        string path = Path.Combine(directory, "ui-state.json");

        UiStateFile.Write(path, new UiState(UiPhase.Deploy, DestructiveStarted: false, Culture: "fr-FR"));

        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
        Assert.Equal("fr-FR", document.RootElement.GetProperty("culture").GetString());
        Assert.Equal(new UiState(UiPhase.Deploy, false, "fr-FR"), UiStateFile.Read(path));
    }

    [Fact]
    public void Write_WithoutCulture_OmitsTheProperty()
    {
        string path = Path.Combine(directory, "ui-state.json");

        UiStateFile.Write(path, new UiState(UiPhase.Connect, DestructiveStarted: false));

        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
        Assert.False(document.RootElement.TryGetProperty("culture", out _));
        Assert.Null(UiStateFile.Read(path)!.Culture);
    }

    [Theory]
    [InlineData("\"culture\":\"xx-INVALID\"")]
    [InlineData("\"culture\":\"not a culture\"")]
    [InlineData("\"culture\":\"es-AR\"")]
    [InlineData("\"culture\":42")]
    [InlineData("\"culture\":null")]
    [InlineData("\"culture\":\"\"")]
    public void Read_UnsupportedOrMalformedCulture_KeepsThePhaseAndDropsTheCulture(string property)
    {
        string path = Path.Combine(directory, "ui-state.json");
        File.WriteAllText(path, "{\"phase\":\"deploy\",\"destructiveStarted\":true," + property + "}");

        Assert.Equal(new UiState(UiPhase.Deploy, DestructiveStarted: true, Culture: null), UiStateFile.Read(path));
    }

    [Fact]
    public void Read_TooLongCulture_DropsTheCulture()
    {
        string path = Path.Combine(directory, "ui-state.json");
        File.WriteAllText(path, "{\"phase\":\"connect\",\"destructiveStarted\":false,\"culture\":\"" + new string('a', 86) + "\"}");

        Assert.Null(UiStateFile.Read(path)!.Culture);
    }

    [Fact]
    public void Read_SupportedCultureInAnotherCase_IsCanonical()
    {
        string path = Path.Combine(directory, "ui-state.json");
        File.WriteAllText(path, "{\"phase\":\"connect\",\"destructiveStarted\":false,\"culture\":\"FR-fr\"}");

        Assert.Equal("fr-FR", UiStateFile.Read(path)!.Culture);
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
