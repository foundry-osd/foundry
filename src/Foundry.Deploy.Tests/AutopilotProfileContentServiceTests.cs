// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Foundry.Core.Services.Autopilot;
using Foundry.Deploy.Models;
using Foundry.Deploy.Services.Autopilot;
using Foundry.Deploy.Services.Security;

namespace Foundry.Deploy.Tests;

public sealed class AutopilotProfileContentServiceTests
{
    [Fact]
    public async Task ReadAsync_WhenProfileIsPlaintext_ReturnsFileContentWithoutUnlockedSession()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "AutopilotConfigurationFile.json");
        const string json = """{"Comment_File":"Legacy"}""";
        await File.WriteAllTextAsync(path, json, TestContext.Current.CancellationToken);
        using var session = new DeploymentSecretKeySession();
        var service = new AutopilotProfileContentService(session);

        byte[] content = await service.ReadAsync(new AutopilotProfileCatalogItem
        {
            FolderName = "Legacy",
            DisplayName = "Legacy",
            ConfigurationFilePath = path
        }, TestContext.Current.CancellationToken);

        Assert.Equal(json, Encoding.UTF8.GetString(content));
        CryptographicOperations.ZeroMemory(content);
        Directory.Delete(root, recursive: true);
    }

    [Fact]
    public async Task ReadAsync_WhenProtectedProfileSessionIsLocked_ReturnsSanitizedFailure()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "AutopilotConfigurationFile.json.encrypted");
        await File.WriteAllTextAsync(path, "encrypted", TestContext.Current.CancellationToken);
        using var session = new DeploymentSecretKeySession();
        var service = new AutopilotProfileContentService(session);

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(() => service.ReadAsync(
            new AutopilotProfileCatalogItem
            {
                FolderName = "Corporate",
                DisplayName = "Corporate",
                ConfigurationFilePath = path,
                IsProtected = true
            },
            TestContext.Current.CancellationToken));

        Assert.Equal("Protected Autopilot profile could not be read.", exception.Message);
        Directory.Delete(root, recursive: true);
    }

    [Fact]
    public async Task ReadAsync_WhenProfileIsProtected_DecryptsWithUnlockedSessionKey()
    {
        string root = CreateTempDirectory();
        byte[] key = RandomNumberGenerator.GetBytes(32);
        using var session = new DeploymentSecretKeySession();
        session.SetKey(key);
        const string json = """{"Comment_File":"Corporate"}""";
        Foundry.Core.Models.Configuration.SecretEnvelope envelope = MediaSecretEnvelopeProtector.EncryptString(
            json,
            key,
            MediaSecretEnvelopeProtector.DeploymentKeyId);
        string path = Path.Combine(root, "AutopilotConfigurationFile.json.encrypted");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(envelope), TestContext.Current.CancellationToken);
        var service = new AutopilotProfileContentService(session);

        byte[] content = await service.ReadAsync(
            new AutopilotProfileCatalogItem
            {
                FolderName = "Corporate",
                DisplayName = "Corporate",
                ConfigurationFilePath = path,
                IsProtected = true
            },
            TestContext.Current.CancellationToken);

        try
        {
            Assert.Equal(json, Encoding.UTF8.GetString(content));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(content);
            CryptographicOperations.ZeroMemory(key);
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ReadAsync_WhenProtectedProfileIsTampered_ReturnsSanitizedFailure()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "AutopilotConfigurationFile.json.encrypted");
        await File.WriteAllTextAsync(path, """{"ciphertext":"secret-value"}""", TestContext.Current.CancellationToken);
        using var session = new DeploymentSecretKeySession();
        session.SetKey(RandomNumberGenerator.GetBytes(32));
        var service = new AutopilotProfileContentService(session);

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(() => service.ReadAsync(
            new AutopilotProfileCatalogItem
            {
                FolderName = "Corporate",
                DisplayName = "Corporate",
                ConfigurationFilePath = path,
                IsProtected = true
            },
            TestContext.Current.CancellationToken));

        Assert.DoesNotContain("secret-value", exception.ToString(), StringComparison.Ordinal);
        Directory.Delete(root, recursive: true);
    }

    private static string CreateTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), $"foundry-profile-content-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
