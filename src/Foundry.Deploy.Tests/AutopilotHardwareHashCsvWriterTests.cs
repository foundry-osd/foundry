using System.Text;
using Foundry.Deploy.Services.Autopilot;

namespace Foundry.Deploy.Tests;

public sealed class AutopilotHardwareHashCsvWriterTests
{
    [Fact]
    public async Task WriteAsync_WhenValuesContainCommas_SanitizesCsvFields()
    {
        using TemporaryWorkspace workspace = new();
        string csvPath = Path.Combine(workspace.RootPath, "AutopilotHWID.csv");

        await AutopilotHardwareHashCsvWriter.WriteAsync(
            csvPath,
            new AutopilotHardwareHashDeviceIdentity("SER,123", "HASHVALUE", "Sales, East"),
            CancellationToken.None);

        byte[] bytes = await File.ReadAllBytesAsync(csvPath);
        string csv = Encoding.UTF8.GetString(bytes);

        Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF);
        Assert.Equal(
            "Device Serial Number,Windows Product ID,Hardware Hash,Group Tag\r\nSER123,,HASHVALUE,Sales East\r\n",
            csv);
        Assert.DoesNotContain('"', csv);
    }

    [Fact]
    public async Task WriteAsync_WhenGroupTagIsNull_WritesEmptyGroupTagColumn()
    {
        using TemporaryWorkspace workspace = new();
        string csvPath = Path.Combine(workspace.RootPath, "AutopilotHWID.csv");

        await AutopilotHardwareHashCsvWriter.WriteAsync(
            csvPath,
            new AutopilotHardwareHashDeviceIdentity("SER123", "HASHVALUE", null),
            CancellationToken.None);

        string[] lines = await File.ReadAllLinesAsync(csvPath);

        Assert.Equal("Device Serial Number,Windows Product ID,Hardware Hash,Group Tag", lines[0]);
        Assert.Equal("SER123,,HASHVALUE,", lines[1]);
    }

    private sealed class TemporaryWorkspace : IDisposable
    {
        public TemporaryWorkspace()
        {
            RootPath = Path.Combine(Path.GetTempPath(), $"foundry-hash-csv-{Guid.NewGuid():N}");
            Directory.CreateDirectory(RootPath);
        }

        public string RootPath { get; }

        public void Dispose()
        {
            Directory.Delete(RootPath, recursive: true);
        }
    }
}
