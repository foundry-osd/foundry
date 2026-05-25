using System.IO;

namespace Foundry.Deploy.Services.Autopilot;

public interface IMediaSecretKeyReader
{
    Task<byte[]> ReadAsync(string workspaceRootPath, CancellationToken cancellationToken = default);
}

/// <summary>
/// Reads the generated boot media secret key from X:\Foundry\Config\Secrets\media-secrets.key.
/// </summary>
public sealed class MediaSecretKeyReader : IMediaSecretKeyReader
{
    private const string KeyRelativePath = @"Config\Secrets\media-secrets.key";

    public async Task<byte[]> ReadAsync(string workspaceRootPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRootPath);

        string keyPath = Path.Combine(workspaceRootPath, KeyRelativePath);
        if (!File.Exists(keyPath))
        {
            throw new FileNotFoundException("Autopilot media secret key was not found in the boot media configuration.", keyPath);
        }

        byte[] key = await File.ReadAllBytesAsync(keyPath, cancellationToken).ConfigureAwait(false);
        if (key.Length != DeployMediaSecretEnvelopeProtector.KeySizeBytes)
        {
            throw new InvalidOperationException("Autopilot media secret key has an invalid length.");
        }

        return key;
    }
}
