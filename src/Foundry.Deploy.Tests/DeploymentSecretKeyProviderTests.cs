// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Security.Cryptography;
using Foundry.Deploy.Models.Configuration;
using Foundry.Deploy.Services.Autopilot;
using Foundry.Deploy.Services.Configuration;
using Foundry.Deploy.Services.Security;

namespace Foundry.Deploy.Tests;

public sealed class DeploymentSecretKeyProviderTests
{
    [Fact]
    public async Task ReadAsync_WhenProtectionIsEnabled_ReturnsUnlockedSessionKey()
    {
        string root = CreateWorkspace();
        byte[] expected = RandomNumberGenerator.GetBytes(32);
        using var session = new DeploymentSecretKeySession();
        session.SetKey(expected);
        var provider = new DeploymentSecretKeyProvider(
            new FakeConfigurationService(isProtected: true),
            session);

        byte[] actual = await provider.ReadAsync(root, TestContext.Current.CancellationToken);

        Assert.Equal(expected, actual);
        CryptographicOperations.ZeroMemory(expected);
        CryptographicOperations.ZeroMemory(actual);
        Directory.Delete(root, recursive: true);
    }

    [Fact]
    public async Task ReadAsync_WhenUnprotectedDeploymentKeyExists_ReturnsDeploymentKey()
    {
        string root = CreateWorkspace();
        byte[] expected = RandomNumberGenerator.GetBytes(32);
        string keyPath = Path.Combine(root, "Config", "Secrets", "deployment-secrets.key");
        Directory.CreateDirectory(Path.GetDirectoryName(keyPath)!);
        await File.WriteAllBytesAsync(keyPath, expected, TestContext.Current.CancellationToken);
        using var session = new DeploymentSecretKeySession();
        var provider = new DeploymentSecretKeyProvider(
            new FakeConfigurationService(isProtected: false),
            session);

        byte[] actual = await provider.ReadAsync(root, TestContext.Current.CancellationToken);

        Assert.Equal(expected, actual);
        CryptographicOperations.ZeroMemory(expected);
        CryptographicOperations.ZeroMemory(actual);
        Directory.Delete(root, recursive: true);
    }

    [Fact]
    public async Task ReadAsync_WhenLegacyMediaHasOnlyMediaKey_ReturnsLegacyKey()
    {
        string root = CreateWorkspace();
        byte[] expected = RandomNumberGenerator.GetBytes(32);
        string keyPath = Path.Combine(root, "Config", "Secrets", "media-secrets.key");
        Directory.CreateDirectory(Path.GetDirectoryName(keyPath)!);
        await File.WriteAllBytesAsync(keyPath, expected, TestContext.Current.CancellationToken);
        using var session = new DeploymentSecretKeySession();
        var provider = new DeploymentSecretKeyProvider(
            new FakeConfigurationService(isProtected: false),
            session);

        byte[] actual = await provider.ReadAsync(root, TestContext.Current.CancellationToken);

        Assert.Equal(expected, actual);
        CryptographicOperations.ZeroMemory(expected);
        CryptographicOperations.ZeroMemory(actual);
        Directory.Delete(root, recursive: true);
    }

    [Fact]
    public async Task ReadAsync_WhenProtectionIsEnabledButSessionIsLocked_DoesNotUseFiles()
    {
        string root = CreateWorkspace();
        string keyPath = Path.Combine(root, "Config", "Secrets", "deployment-secrets.key");
        Directory.CreateDirectory(Path.GetDirectoryName(keyPath)!);
        await File.WriteAllBytesAsync(keyPath, RandomNumberGenerator.GetBytes(32), TestContext.Current.CancellationToken);
        using var session = new DeploymentSecretKeySession();
        var provider = new DeploymentSecretKeyProvider(
            new FakeConfigurationService(isProtected: true),
            session);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.ReadAsync(root, TestContext.Current.CancellationToken));

        Directory.Delete(root, recursive: true);
    }

    private static string CreateWorkspace()
    {
        string path = Path.Combine(Path.GetTempPath(), $"foundry-deploy-key-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class FakeConfigurationService(bool isProtected) : IDeployConfigurationService
    {
        public DeployConfigurationLoadResult LoadOptional() => new()
        {
            Exists = true,
            Document = new FoundryDeployConfigurationDocument
            {
                Protection = new DeployProtectionSettings { IsEnabled = isProtected }
            }
        };
    }
}
