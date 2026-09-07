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
    private const string ValidJson = """
        {
          "CloudAssignedTenantId": "11111111-1111-4111-8111-111111111111",
          "CloudAssignedForcedEnrollment": 1,
          "Version": 2049,
          "Comment_File": "Profile Synthetic",
          "CloudAssignedAadServerData": "{\"ZeroTouchConfig\":{\"CloudAssignedTenantUpn\":\"\",\"ForcedEnrollment\":1,\"CloudAssignedTenantDomain\":\"example.onmicrosoft.com\"}}",
          "CloudAssignedTenantDomain": "example.onmicrosoft.com",
          "CloudAssignedDomainJoinMethod": 0,
          "CloudAssignedOobeConfig": 28,
          "ZtdCorrelationId": "22222222-2222-4222-8222-222222222222"
        }
        """;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReadAsync_UnsupportedOfflineContent_IsRejectedForPlaintextAndEncryptedProfiles(bool encrypted)
    {
        string root = CreateTempDirectory();
        try
        {
            string path = Path.Combine(root, "profile.json");
            using var session = new DeploymentSecretKeySession();
            byte[] key = RandomNumberGenerator.GetBytes(32);
            session.SetKey(key);
            string content = encrypted ? JsonSerializer.Serialize(MediaSecretEnvelopeProtector.EncryptString("{}", key, MediaSecretEnvelopeProtector.DeploymentKeyId)) : "{}";
            CryptographicOperations.ZeroMemory(key);
            await File.WriteAllTextAsync(path, content, TestContext.Current.CancellationToken);
            await Assert.ThrowsAsync<InvalidDataException>(() => new AutopilotProfileContentService(session).ReadAsync(new()
            {
                ConfigurationFilePath = path,
                IsProtected = encrypted,
                FolderName = "Synthetic",
                DisplayName = "Synthetic"
            }, TestContext.Current.CancellationToken));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task ReadAsync_WhenProfileIsPlaintext_ReturnsFileContentWithoutUnlockedSession()
    {
        string root = CreateTempDirectory();
        string path = Path.Combine(root, "AutopilotConfigurationFile.json");
        const string json = ValidJson;
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
        const string json = ValidJson;
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
